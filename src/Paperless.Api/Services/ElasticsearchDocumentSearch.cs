using System.Net.Http.Json;
using System.Text.Json;

namespace Paperless.Api.Services;

public sealed class ElasticsearchDocumentSearch(HttpClient httpClient)
{
    public async Task<IReadOnlyList<DocumentSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<DocumentSearchResult>();

        using var response = await httpClient.PostAsJsonAsync("/paperless-documents/_search", new
        {
            size = 100,
            query = new
            {
                multi_match = new
                {
                    query,
                    fields = new[] { "fileName^3", "description", "textContent" }
                }
            }
        }, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var result = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var hits = result.RootElement.GetProperty("hits").GetProperty("hits");
        return hits.EnumerateArray().Select(hit =>
        {
            var source = hit.GetProperty("_source");
            return new DocumentSearchResult(
                source.GetProperty("documentId").GetGuid(),
                source.GetProperty("fileName").GetString() ?? string.Empty,
                source.TryGetProperty("description", out var description) ? description.GetString() : null);
        }).ToArray();
    }
}

public sealed record DocumentSearchResult(Guid Id, string FileName, string? Description);