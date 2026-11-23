using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Paperless.Api.DTOs;
using Paperless.Api.Services;

namespace Paperless.Api.Controllers;

//: ======================================================================
//: Reads and updates provider settings while keeping API keys masked in responses.
//: ======================================================================
[ApiController]
[Route("api/genai/settings")]
public class GenAiSettingsController(IHostEnvironment environment) : ControllerBase
{
    private readonly string _path = Environment.GetEnvironmentVariable("GENAI_SETTINGS_PATH")
        ?? Path.Combine(environment.ContentRootPath, "App_Data", "genai-settings.json");

    // Return the saved providers without exposing their full credentials.
    // ####################################################################################
    // Provider settings API
    // ####################################################################################
    //* Returns saved providers without exposing full API keys.
    [HttpGet]
    public async Task<ActionResult<GenAiSettingsResponse>> Get(CancellationToken ct)
    {
        var settings = await ReadAsync(ct);
        var publicProviders = settings.Providers.Select(provider => provider with
        {
            ApiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? null : $"{provider.ApiKey[..Math.Min(3, provider.ApiKey.Length)]}••••••••"
        }).ToArray();
        return Ok(new GenAiSettingsResponse(publicProviders));
    }

    //* Validates, normalizes, and persists submitted provider settings.
    [HttpPut]
    public async Task<ActionResult<GenAiSettingsResponse>> Update(GenAiSettingsRequest request, CancellationToken ct)
    {
        var current = await ReadAsync(ct);
        var promptDefault = "Create a detailed, useful summary of the document. Write several short paragraphs and use clear bullet points for the main facts. Include key people, dates, organizations, education or employment, qualifications, conclusions, and important numbers. Do not invent information. Use complete sentences and a professional tone.";

        var providers = (request.Providers ?? Array.Empty<GenAiProviderRequest>())
            .Select(provider =>
            {
                var id = string.IsNullOrWhiteSpace(provider.Id) ? provider.Provider : provider.Id.Trim();
                var previous = current.Providers.FirstOrDefault(p => p.Id == id);
                var apiKey = provider.ApiKey == "__clear__"
                    ? null
                    : (string.IsNullOrWhiteSpace(provider.ApiKey) || provider.ApiKey == "••••••••"
                        || (provider.ApiKey.Contains("••") && provider.ApiKey.Length <= 16)
                        ? previous?.ApiKey
                        : provider.ApiKey.Trim());
                var providerType = string.IsNullOrWhiteSpace(provider.Provider) ? "local" : provider.Provider.Trim().ToLowerInvariant();
                return new GenAiProviderRequest(
                    id,
                    string.IsNullOrWhiteSpace(provider.Name) ? id : provider.Name.Trim(),
                    providerType,
                    apiKey,
                    string.IsNullOrWhiteSpace(provider.Model)
                        ? (providerType == "gemini" ? "gemini-2.5-flash" : "llama3.1")
                        : provider.Model.Trim(),
                    string.IsNullOrWhiteSpace(provider.BaseUrl) ? null : provider.BaseUrl.Trim(),
                    string.IsNullOrWhiteSpace(provider.Prompt) ? promptDefault : provider.Prompt.Trim(),
                    provider.Enabled);
            })
            .ToArray();

        foreach (var provider in providers)
        {
            if (provider.Enabled && provider.Provider == "gemini" && string.IsNullOrWhiteSpace(provider.ApiKey))
                return BadRequest("Enter a Gemini API key before enabling Gemini.");
            if (provider.Enabled && provider.Provider == "local" && string.IsNullOrWhiteSpace(provider.BaseUrl))
                return BadRequest("Enter a local server URL before enabling the local provider.");
            if (provider.Enabled && provider.Provider == "local" && provider.BaseUrl?.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) == true)
                return BadRequest("Gemini API requests must use the Google Gemini provider card, not a local OpenAI-compatible provider.");
        }

        var enabledCount = providers.Count(provider => provider.Enabled);
        if (enabledCount > 1)
        {
            var firstEnabled = true;
            providers = providers.Select(provider =>
            {
                if (!provider.Enabled) return provider;
                var enabled = firstEnabled;
                firstEnabled = false;
                return provider with { Enabled = enabled };
            }).ToArray();
        }

        var settings = new GenAiSettingsRequest(providers);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = System.IO.File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true }, ct);

        var masked = providers.Select(provider => provider with
        {
            ApiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? null : "••••••••"
        }).ToArray();
        return Ok(new GenAiSettingsResponse(masked));
    }

    // Load current and legacy settings formats, falling back to disabled defaults.
    // ####################################################################################
    // Settings persistence and backward-compatible reads
    // ####################################################################################
    private async Task<GenAiSettingsRequest> ReadAsync(CancellationToken ct)
    {
        var defaultPrompt = "Create a detailed, useful summary of the document. Write several short paragraphs and use clear bullet points for the main facts. Include key people, dates, organizations, education or employment, qualifications, conclusions, and important numbers. Do not invent information. Use complete sentences and a professional tone.";
        var defaults = new[]
        {
            new GenAiProviderRequest("gemini", "Google Gemini", "gemini", null, "gemini-2.5-flash", null, defaultPrompt, false),
            new GenAiProviderRequest("local", "Local / OpenAI-compatible", "local", null, "llama3.1", null, defaultPrompt, false)
        };

        if (!System.IO.File.Exists(_path))
            return new GenAiSettingsRequest(defaults);

        await using var stream = System.IO.File.OpenRead(_path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (document.RootElement.TryGetProperty("providers", out var providersElement)
            || document.RootElement.TryGetProperty("Providers", out providersElement))
        {
            var providers = JsonSerializer.Deserialize<IReadOnlyList<GenAiProviderRequest>>(providersElement.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (providers is { Count: > 0 }) return new GenAiSettingsRequest(providers);
        }

        var legacy = JsonSerializer.Deserialize<LegacyGenAiSettings>(document.RootElement.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (legacy is null) return new GenAiSettingsRequest(defaults);

        if (legacy.Provider == "gemini")
            defaults[0] = defaults[0] with { ApiKey = legacy.ApiKey, Model = legacy.Model, Prompt = legacy.Prompt, Enabled = legacy.Enabled };
        else
            defaults[1] = defaults[1] with { ApiKey = legacy.ApiKey, Model = legacy.Model, BaseUrl = legacy.BaseUrl, Prompt = legacy.Prompt, Enabled = legacy.Enabled };
        return new GenAiSettingsRequest(defaults);
    }

    private sealed record LegacyGenAiSettings(string? Provider, string? ApiKey, string? Model, string? BaseUrl, string? Prompt, bool Enabled);

    // ####################################################################################
    // In-memory worker diagnostics
    // ####################################################################################
    // Keep a short in-memory history of worker request/response diagnostics.
    [HttpPost("/api/genai/diagnostics")]
    public ActionResult PostDiagnostic([FromBody] JsonElement diagnostic)
    {
        GenAiDiagnostics.Store(diagnostic.Clone());
        return Ok();
    }

    [HttpGet("/api/genai/diagnostics")]
    //* Returns the stored worker diagnostics.
    public ActionResult<IReadOnlyList<JsonElement>> GetDiagnostics()
    {
        return Ok(GenAiDiagnostics.GetAll());
    }
}
