using Microsoft.AspNetCore.Mvc;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SkiaSharp;
using Paperless.Api.DTOs;
using Paperless.Api.Entities;
using Paperless.Api.Queues;
using Paperless.Api.Repositories;
using Paperless.Api.Storage;
using OpenCvSharp;

namespace Paperless.Api.Controllers;

//: ======================================================================
//: PDF and image editing endpoints, including in-place overlays and edited uploads.
//: ======================================================================
[ApiController]
[Route("api/documents")]
public class DocumentEditingController(
    IDocumentRepository documents,
    IDocumentStorage storage,
    IRabbitMqDocumentMessagePublisher messagePublisher) : ControllerBase
{
    // Rebuild a PDF copy from the text submitted by the browser editor.
    [HttpPost("{id:guid}/edit/pdf")]
    public async Task<ActionResult<DocumentResponse>> EditPdf(Guid id, PdfEditRequest request, CancellationToken ct)
    {
        var original = await documents.GetByIdAsync(id, ct);
        if (original is null) return NotFound();

        await using var originalStream = await storage.GetAsync(original.StoragePath, ct);
        if (originalStream is null) return NotFound("Original document storage object was not found.");

        using var pdf = new PdfDocument();
        pdf.Info.Title = request.FileName ?? $"edited-{original.FileName}";
        var page = pdf.AddPage();
        page.Size = PdfSharpCore.PageSize.A4;

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("DejaVu Sans", 11, XFontStyle.Regular);
        var brush = XBrushes.Black;
        var margin = 48;
        var maxWidth = page.Width - margin * 2;
        var y = margin;

        foreach (var paragraph in (request.Text ?? string.Empty).Replace("\r", "").Split('\n'))
        {
            var text = paragraph.TrimEnd();
            if (string.IsNullOrWhiteSpace(text))
            {
                y += 14;
                continue;
            }

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = "";
            foreach (var word in words)
            {
                var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
                if (gfx.MeasureString(candidate, font).Width <= maxWidth)
                {
                    line = candidate;
                }
                else
                {
                    gfx.DrawString(line, font, brush, new XRect(margin, y, maxWidth, 18));
                    y += 18;
                    line = word;
                    if (y > page.Height - margin)
                    {
                        break;
                    }
                }
            }
            if (!string.IsNullOrEmpty(line)) gfx.DrawString(line, font, brush, new XRect(margin, y, maxWidth, 18));
            y += 20;
        }

        await using var output = new MemoryStream();
        pdf.Save(output);
        output.Position = 0;

        var fileName = string.IsNullOrWhiteSpace(request.FileName)
            ? $"edited-{original.FileName}".Replace(".pdf", "-edited.pdf", StringComparison.OrdinalIgnoreCase)
            : request.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? request.FileName : request.FileName + ".pdf";
        var edited = new Document
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            ContentType = "application/pdf",
            StoragePath = $"documents/{Guid.NewGuid():N}/{fileName}",
            Description = $"Edited copy of {original.FileName}",
            OcrText = request.Text,
            UploadedAtUtc = DateTime.UtcNow
        };
        await storage.PutAsync(edited.StoragePath, output, output.Length, "application/pdf", ct);
        await documents.AddAsync(edited, ct);
        await documents.SaveChangesAsync(ct);
        messagePublisher.PublishDocumentUploaded(edited.Id, edited.FileName, edited.Description, edited.StoragePath);
        return StatusCode(201, edited.Id);
    }

    // Apply submitted text regions to an image and store the result as a new copy.
    [HttpPost("{id:guid}/edit/image")]
    public async Task<ActionResult<DocumentResponse>> EditImage(Guid id, ImageEditRequest request, CancellationToken ct)
    {
        var original = await documents.GetByIdAsync(id, ct);
        if (original is null) return NotFound();
        await using var source = await storage.GetAsync(original.StoragePath, ct);
        if (source is null) return NotFound("Original image was not found.");

        using var sourceBytes = new MemoryStream();
        await source.CopyToAsync(sourceBytes, ct);
        using var cvSource = Cv2.ImDecode(sourceBytes.ToArray(), ImreadModes.Color);
        if (cvSource is null || cvSource.Empty()) return BadRequest("The document is not a supported image.");

        using var mask = new OpenCvSharp.Mat(cvSource.Rows, cvSource.Cols, OpenCvSharp.MatType.CV_8UC1, new Scalar(0));
        foreach (var overlay in request.Overlays ?? Array.Empty<ImageTextOverlay>())
        {
            var x = (int)Math.Round(cvSource.Cols * overlay.X);
            var y = (int)Math.Round(cvSource.Rows * overlay.Y);
            var width = (int)Math.Round(cvSource.Cols * overlay.Width);
            var height = (int)Math.Round(cvSource.Rows * overlay.Height);
            var pad = Math.Max(2, (int)Math.Round(height * 0.12));
            var left = Math.Max(0, x - pad);
            var top = Math.Max(0, y - pad);
            var right = Math.Min(cvSource.Cols, x + width + pad);
            var bottom = Math.Min(cvSource.Rows, y + height + pad);
            var rect = new OpenCvSharp.Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
            if (rect.Width > 0 && rect.Height > 0) Cv2.Rectangle(mask, rect, Scalar.White, -1);
        }

        using var inpainted = new OpenCvSharp.Mat();
        Cv2.Inpaint(cvSource, mask, inpainted, 7, InpaintMethod.Telea);
        if (!Cv2.ImEncode(".png", inpainted, out var encoded)) return BadRequest("Could not encode edited image.");

        using var bitmap = SKBitmap.Decode(encoded);
        if (bitmap is null) return BadRequest("The edited image could not be created.");

        using var canvas = new SKCanvas(bitmap);
        foreach (var overlay in request.Overlays ?? Array.Empty<ImageTextOverlay>())
        {
            if (string.IsNullOrWhiteSpace(overlay.Text)) continue;
            var x = bitmap.Width * overlay.X;
            var y = bitmap.Height * overlay.Y;
            var width = bitmap.Width * overlay.Width;
            var height = bitmap.Height * overlay.Height;

            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = ParseColor(overlay.Color),
                TextSize = Math.Max(8, height * 0.72f),
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal),
                SubpixelText = true
            };
            var measured = paint.MeasureText(overlay.Text);
            if (measured > width && measured > 0) paint.TextSize *= width / measured;
            canvas.DrawText(overlay.Text, x, y + height * 0.82f, paint);
        }

        await using var imageOutput = new MemoryStream();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        data.SaveTo(imageOutput);
        imageOutput.Position = 0;

        await using var stream = new MemoryStream(imageOutput.ToArray());
        var copy = await BuildCopyLocationAsync(original, ".png", ct);
        var fileName = copy.FileName;
        var edited = new Document
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            ContentType = "image/png",
            StoragePath = copy.StoragePath,
            Description = $"Edited copy of {original.FileName}",
            UploadedAtUtc = DateTime.UtcNow
        };
        await storage.PutAsync(edited.StoragePath, stream, stream.Length, "image/png", ct);
        await documents.AddAsync(edited, ct);
        await documents.SaveChangesAsync(ct);
        messagePublisher.PublishDocumentUploaded(edited.Id, edited.FileName, edited.Description, edited.StoragePath);
        return StatusCode(201, edited.Id);
    }

    // Store the fully edited PDF produced by the client-side PDF editor.
    [HttpPost("{id:guid}/edit/pdf/upload")]
    public async Task<ActionResult<Guid>> UploadEditedPdf(Guid id, IFormFile file, [FromForm] string? fileName, CancellationToken ct)
    {
        var original = await documents.GetByIdAsync(id, ct);
        if (original is null) return NotFound();
        if (file is null || file.Length == 0) return BadRequest("No PDF file was provided.");

        var copy = await BuildCopyLocationAsync(original, ".pdf", ct);
        var name = copy.FileName;

        await using var stream = file.OpenReadStream();
        var edited = new Document
        {
            Id = Guid.NewGuid(),
            FileName = name,
            ContentType = "application/pdf",
            StoragePath = copy.StoragePath,
            Description = $"Edited copy of {original.FileName}",
            OcrText = original.OcrText,
            UploadedAtUtc = DateTime.UtcNow
        };
        await storage.PutAsync(edited.StoragePath, stream, file.Length, "application/pdf", ct);
        await documents.AddAsync(edited, ct);
        await documents.SaveChangesAsync(ct);
        messagePublisher.PublishDocumentUploaded(edited.Id, edited.FileName, edited.Description, edited.StoragePath);
        return StatusCode(201, edited.Id);
    }

    // Parse a CSS-style hex color and fall back to readable dark text.
    private static SKColor ParseColor(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && SKColor.TryParse(value, out var color)) return color;
        return SKColors.DarkSlateGray;
    }

    // Generate a unique sibling name and storage key for an edited copy.
    private async Task<(string FileName, string StoragePath)> BuildCopyLocationAsync(Document original, string extension, CancellationToken ct)
    {
        var all = await documents.GetAllAsync(ct);
        var slash = original.StoragePath.LastIndexOf('/');
        var directory = slash > 0 ? original.StoragePath[..slash].TrimEnd('/') : "documents";
        var siblingCopies = all.Count(document =>
            document.StoragePath.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase)
            && document.FileName.StartsWith("kopie_", StringComparison.OrdinalIgnoreCase));
        var number = siblingCopies + 1;

        var baseName = Path.GetFileNameWithoutExtension(original.FileName);
        if (baseName.StartsWith("kopie_", StringComparison.OrdinalIgnoreCase))
        {
            var firstUnderscore = baseName.IndexOf('_');
            var secondUnderscore = firstUnderscore >= 0 ? baseName.IndexOf('_', firstUnderscore + 1) : -1;
            if (secondUnderscore > 0) baseName = baseName[(secondUnderscore + 1)..];
        }

        var fileName = $"kopie_{number:00}_{baseName}{extension}";
        return (fileName, $"{directory}/{fileName}");
    }
}
