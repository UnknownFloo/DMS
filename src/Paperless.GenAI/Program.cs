using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

// ############################
// Consume summary requests and send them to the selected configured AI provider.
// ############################
const string queueName = "document-genai-request";
var rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
var rabbitPort = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var parsedPort) ? parsedPort : 5672;
var rabbitUser = Environment.GetEnvironmentVariable("RABBITMQ_USERNAME") ?? "guest";
var rabbitPassword = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest";
var apiUrl = Environment.GetEnvironmentVariable("PAPERLESS_API_URL") ?? "http://api:8080";
var settingsPath = Environment.GetEnvironmentVariable("GENAI_SETTINGS_PATH") ?? "/app/App_Data/genai-settings.json";

var factory = new ConnectionFactory { HostName = rabbitHost, Port = rabbitPort, UserName = rabbitUser, Password = rabbitPassword, AutomaticRecoveryEnabled = true };
using var connection = await factory.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync();
await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
await channel.BasicQosAsync(0, 1, false);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

Console.WriteLine($"[genai] Listening on {rabbitHost}:{rabbitPort}/{queueName}");
// Generate and store each summary before acknowledging its queue message.
var consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += async (_, eventArgs) =>
{
    try
    {
        var message = JsonSerializer.Deserialize<OcrCompletedMessage>(eventArgs.Body.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Empty GenAI request.");
        var settings = await ReadSettingsAsync(settingsPath);
        if (!settings.Enabled)
        {
            Console.WriteLine($"[genai] Disabled; document {message.DocumentId} skipped.");
            await channel.BasicAckAsync(eventArgs.DeliveryTag, false);
            return;
        }

        var text = message.TextContent ?? message.Text ?? throw new InvalidDataException("GenAI request has no OCR text.");
        var settingsWithPrompt = string.IsNullOrWhiteSpace(message.Prompt)
            ? settings
            : settings with { Prompt = message.Prompt };
        var summary = await GenerateSummaryAsync(http, settingsWithPrompt, text, apiUrl, message.DocumentId);
        await SaveSummaryAsync(http, apiUrl, message.DocumentId, summary);
        await channel.BasicAckAsync(eventArgs.DeliveryTag, false);
        Console.WriteLine($"[genai] Summary stored for {message.FileName} ({summary.Length} characters).");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"[genai] Failed to generate/store summary: {exception}");
        await channel.BasicNackAsync(eventArgs.DeliveryTag, false, requeue: false);
    }
};
await channel.BasicConsumeAsync(queueName, false, consumer);

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stopping.Cancel(); };
await Task.Delay(Timeout.Infinite, stopping.Token);

// Support the current provider list and the older single-provider settings file.
static async Task<GenAiSettings> ReadSettingsAsync(string path)
{
    var defaultPrompt = "Create a detailed, useful summary of the document. Write several short paragraphs and use clear bullet points for the main facts. Include key people, dates, organizations, education or employment, qualifications, conclusions, and important numbers. Do not invent information. Use complete sentences and a professional tone.";
    if (!File.Exists(path)) return new GenAiSettings("gemini", null, "gemini-4-hydrogen", null, defaultPrompt, false);

    await using var stream = File.OpenRead(path);
    using var document = await JsonDocument.ParseAsync(stream);

    if (document.RootElement.TryGetProperty("providers", out var providersElement)
        || document.RootElement.TryGetProperty("Providers", out providersElement))
    {
        var providers = JsonSerializer.Deserialize<IReadOnlyList<GenAiProvider>>(providersElement.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var enabled = providers?.FirstOrDefault(provider => provider.Enabled
            && !(provider.Provider == "local"
                && (provider.BaseUrl?.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) == true
                    || provider.Model?.StartsWith("gemini", StringComparison.OrdinalIgnoreCase) == true)));
        if (enabled is not null)
        {
            return new GenAiSettings(enabled.Provider, enabled.ApiKey, enabled.Model, enabled.BaseUrl,
                string.IsNullOrWhiteSpace(enabled.Prompt) ? defaultPrompt : enabled.Prompt, true);
        }
        return new GenAiSettings("gemini", null, "gemini-4-hydrogen", null, defaultPrompt, false);
    }

    var legacy = JsonSerializer.Deserialize<GenAiSettings>(document.RootElement.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    return legacy ?? new GenAiSettings("gemini", null, "gemini-4-hydrogen", null, defaultPrompt, false);
}

// Build the shared prompt, then dispatch to the OpenAI-compatible or Gemini API.
static async Task<string> GenerateSummaryAsync(HttpClient http, GenAiSettings settings, string text, string apiUrl, Guid documentId)
{
    if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("OCR text is empty.");
    var prompt = $"{settings.Prompt}\n\nDOCUMENT TEXT:\n{text[..Math.Min(text.Length, 30000)]}";

    Console.WriteLine($"[genai] Request for document {settings.Provider}: provider={settings.Provider}, model={settings.Model}, enabled={settings.Enabled}, OCR chars={text.Length}, prompt chars={prompt.Length}");

    var textHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..16];
    var promptHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(prompt))).ToLowerInvariant()[..16];
    Console.WriteLine($"[genai] Provenance OCR chars={text.Length}, OCR sha256={textHash}, prompt chars={prompt.Length}, prompt sha256={promptHash}");

    if (settings.Provider == "local")
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl)) throw new InvalidOperationException("A local GenAI base URL is required.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = settings.Model,
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.2
            })
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey)) request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");
        Console.WriteLine($"[genai] Sending local request to {settings.BaseUrl.TrimEnd('/')}/chat/completions, model={settings.Model}");
        using var response = await http.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"[genai] Local response status={(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Local GenAI returned {(int)response.StatusCode}: {responseText}");
        using var localJson = JsonDocument.Parse(responseText);
        var localSummary = localJson.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return ValidateSummary(localSummary);
    }

    if (string.IsNullOrWhiteSpace(settings.ApiKey)) throw new InvalidOperationException("A Gemini API key is required.");
    var configuredModel = string.IsNullOrWhiteSpace(settings.Model) ? "gemini-2.5-flash" : settings.Model;
    var models = new[] { configuredModel, "gemini-2.5-flash", "gemini-2.0-flash" }.Distinct(StringComparer.OrdinalIgnoreCase);
    var failures = new List<string>();

    foreach (var model in models)
    {
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? "https://generativelanguage.googleapis.com"
            : settings.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/v1beta/models/{model}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", settings.ApiKey);
        var requestBody = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { temperature = 0.2, maxOutputTokens = 4096 }
        };
        request.Content = JsonContent.Create(requestBody);

        var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Console.WriteLine($"[genai] Gemini request body: {requestJson}");
        await ReportDiagnosticAsync(http, apiUrl, new { documentId, provider = settings.Provider, model, url, requestBody = requestJson, status = "sending", atUtc = DateTime.UtcNow });
        Console.WriteLine($"[genai] Sending Gemini request to {url}, model={model}, prompt chars={prompt.Length}");
        using var geminiResponse = await http.SendAsync(request);
        var geminiText = await geminiResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"[genai] Gemini response status={(int)geminiResponse.StatusCode}, model={model}, response chars={geminiText.Length}");
        await ReportDiagnosticAsync(http, apiUrl, new { documentId, provider = settings.Provider, model, url, httpStatus = (int)geminiResponse.StatusCode, responseChars = geminiText.Length, status = geminiResponse.IsSuccessStatusCode ? "response-ok" : "response-error", atUtc = DateTime.UtcNow });

        if (!geminiResponse.IsSuccessStatusCode)
        {
            failures.Add($"{model}: {(int)geminiResponse.StatusCode} {geminiText[..Math.Min(geminiText.Length, 300)]}");
            continue;
        }

        using var geminiJson = JsonDocument.Parse(geminiText);
        var candidates = geminiJson.RootElement.TryGetProperty("candidates", out var candidateElement)
            ? candidateElement
            : default;
        if (candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) continue;
        var parts = candidates[0].GetProperty("content").GetProperty("parts");
        if (parts.GetArrayLength() == 0) continue;
        var textResult = parts[0].GetProperty("text").GetString();
        return ValidateSummary(textResult);
    }

    throw new HttpRequestException($"Gemini summary failed. {string.Join(" | ", failures)}");
}

// Reject empty provider responses before attempting to save them.
static string ValidateSummary(string? summary)
{
    if (string.IsNullOrWhiteSpace(summary)) throw new InvalidOperationException("GenAI returned an empty summary.");
    return summary.Trim();
}

// Persist the generated summary through the Paperless API.
static async Task SaveSummaryAsync(HttpClient http, string apiUrl, Guid documentId, string summary)
{
    using var response = await http.PutAsJsonAsync($"{apiUrl.TrimEnd('/')}/api/documents/{documentId:D}/summary", new { summary });
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Paperless API returned {(int)response.StatusCode}: {body}");
}

// Diagnostics are best-effort and must not fail a summary request.
static async Task ReportDiagnosticAsync(HttpClient http, string apiUrl, object payload)
{
    try
    {
        await http.PostAsJsonAsync($"{apiUrl.TrimEnd('/')}/api/genai/diagnostics", payload);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"[genai] Could not report diagnostic: {exception.Message}");
    }
}

internal sealed record OcrCompletedMessage(Guid DocumentId, string FileName, string? Description, string? TextContent, string? Text, string? Prompt);
internal sealed record GenAiSettings(string Provider, string? ApiKey, string Model, string? BaseUrl, string Prompt, bool Enabled);
internal sealed record GenAiProvider(string Id, string Name, string Provider, string? ApiKey, string Model, string? BaseUrl, string Prompt, bool Enabled);
