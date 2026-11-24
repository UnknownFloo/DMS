namespace Paperless.Api.DTOs;

//: ======================================================================
//: Request and response contracts for document operations and GenAI settings.
//: ======================================================================
public record CreateDocumentRequest(string FileName, string? ContentType, string? Description, IReadOnlyCollection<string>? Tags);
public record UpdateDocumentRequest(string FileName, string? Description);
public record SummaryUpdateRequest(string? Summary);
public record GenAiProviderRequest(string Id, string Name, string Provider, string? ApiKey, string Model, string? BaseUrl, string Prompt, bool Enabled);
public record GenAiSettingsRequest(IReadOnlyList<GenAiProviderRequest> Providers);
public record GenAiSettingsResponse(IReadOnlyList<GenAiProviderRequest> Providers);
public record DocumentResponse(Guid Id, string FileName, string ContentType, string StoragePath, string? Description, string? Summary, string? OcrText, DateTime UploadedAtUtc, DateTime? UpdatedAtUtc, IReadOnlyCollection<string> Tags);
public record OcrTextUpdateRequest(string? Text);
public record SummaryRegenerateRequest(string Prompt);
public record PdfEditRequest(string Text, string? FileName);
public record ImageTextOverlay(float X, float Y, float Width, float Height, string Text, string Color);
public record ImageEditRequest(IReadOnlyList<ImageTextOverlay> Overlays, string? FileName);
