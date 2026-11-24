using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Minio;
using Minio.DataModel.Args;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

// ############################
// Queue names form the message contract between the API and background services.
// ############################
const string uploadQueue = "document-uploaded";
const string ocrQueue = "document-ocr-completed";
const string genAiQueue = "document-genai-request";
var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
var port = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var parsedPort) ? parsedPort : 5672;
var user = Environment.GetEnvironmentVariable("RABBITMQ_USERNAME") ?? "guest";
var password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest";
var storageEndpoint = Environment.GetEnvironmentVariable("OBJECT_STORAGE_ENDPOINT") ?? "localhost:9000";
var storageAccessKey = Environment.GetEnvironmentVariable("OBJECT_STORAGE_ACCESS_KEY") ?? "paperless";
var storageSecretKey = Environment.GetEnvironmentVariable("OBJECT_STORAGE_SECRET_KEY") ?? "paperless-secret-key";
var storageBucket = Environment.GetEnvironmentVariable("OBJECT_STORAGE_BUCKET") ?? "paperless-documents";
var storageUseSsl = string.Equals(Environment.GetEnvironmentVariable("OBJECT_STORAGE_USE_SSL"), "true", StringComparison.OrdinalIgnoreCase);
var apiUrl = Environment.GetEnvironmentVariable("PAPERLESS_API_URL") ?? "http://api:8080";

// Connect to RabbitMQ and object storage once, then reuse them for each upload.
var factory = new ConnectionFactory { HostName = host, Port = port, UserName = user, Password = password };
using var connection = await factory.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync();
var storage = new MinioClient()
    .WithEndpoint(storageEndpoint)
    .WithCredentials(storageAccessKey, storageSecretKey)
    .WithSSL(storageUseSsl)
    .Build();

await channel.QueueDeclareAsync(uploadQueue, durable: true, exclusive: false, autoDelete: false, arguments: null);
await channel.QueueDeclareAsync(ocrQueue, durable: true, exclusive: false, autoDelete: false, arguments: null);
await channel.QueueDeclareAsync(genAiQueue, durable: true, exclusive: false, autoDelete: false, arguments: null);
await channel.BasicQosAsync(0, 1, false);

Console.WriteLine($"OCR worker listening on RabbitMQ {host}:{port} / queue {uploadQueue}");

// Process one upload at a time: OCR, publish downstream events, then acknowledge.
var consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += async (_, eventArgs) =>
{
    try
    {
        var message = JsonSerializer.Deserialize<UploadMessage>(eventArgs.Body.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Empty upload message.");
        if (string.IsNullOrWhiteSpace(message.StoragePath))
            throw new InvalidDataException($"Upload message for {message.DocumentId} has no storage path.");

        var text = await RecognizeDocumentAsync(storage, storageBucket, message.StoragePath, message.FileName, CancellationToken.None);
        var result = new OcrCompletedMessage(message.DocumentId, message.FileName, message.Description, text);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await channel.BasicPublishAsync(string.Empty, ocrQueue, false, new BasicProperties { Persistent = true }, body, CancellationToken.None);

        // Publish to GenAI queue
        var genAiBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { message.DocumentId, message.FileName, TextContent = text }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await channel.BasicPublishAsync(string.Empty, genAiQueue, false, new BasicProperties { Persistent = true }, genAiBody, CancellationToken.None);

        using var apiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        await apiClient.PutAsJsonAsync($"{apiUrl.TrimEnd('/')}/api/documents/{message.DocumentId:D}/ocr-text", new { text });

        await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false);
        Console.WriteLine($"[ocr] Recognized {message.FileName} ({text.Length} characters).");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"[ocr] Failed to process message: {exception}");
        await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false);
    }
};

await channel.BasicConsumeAsync(uploadQueue, autoAck: false, consumer: consumer, cancellationToken: CancellationToken.None);

Console.WriteLine("Worker started. Press Ctrl+C to stop.");
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.Cancel();
};
await Task.Delay(Timeout.Infinite, stopping.Token);

// Route supported uploads to the image or PDF OCR pipeline.
static async Task<string> RecognizeDocumentAsync(IMinioClient storage, string bucket, string storagePath, string fileName, CancellationToken cancellationToken)
{
    var extension = Path.GetExtension(fileName).ToLowerInvariant();
    if (extension == ".pdf")
        return await RecognizePdfAsync(storage, bucket, storagePath, cancellationToken);

    var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".webp"
    };
    if (!imageExtensions.Contains(extension))
        throw new InvalidDataException($"Unsupported OCR file type: {extension}");

    var workingDirectory = Path.Combine(Path.GetTempPath(), $"paperless-image-ocr-{Guid.NewGuid():N}");
    Directory.CreateDirectory(workingDirectory);
    var imagePath = Path.Combine(workingDirectory, $"document{extension}");
    try
    {
        await using var image = File.Create(imagePath);
        await storage.GetObjectAsync(new GetObjectArgs()
            .WithBucket(bucket)
            .WithObject(storagePath)
            .WithCallbackStream(stream => stream.CopyTo(image)), cancellationToken);

        var result = await RunProcessAsync("tesseract", [imagePath, "stdout", "-l", "eng+deu", "--psm", "6"], cancellationToken);
        var text = result.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("Tesseract did not recognize text in the image.");
        return text;
    }
    finally
    {
        Directory.Delete(workingDirectory, recursive: true);
    }
}

// Render PDF pages to temporary images before recognizing each page with Tesseract.
static async Task<string> RecognizePdfAsync(IMinioClient storage, string bucket, string storagePath, CancellationToken cancellationToken)
{
    var workingDirectory = Path.Combine(Path.GetTempPath(), $"paperless-ocr-{Guid.NewGuid():N}");
    Directory.CreateDirectory(workingDirectory);
    var pdfPath = Path.Combine(workingDirectory, "document.pdf");
    try
    {
        await using (var pdf = File.Create(pdfPath))
        {
            await storage.GetObjectAsync(new GetObjectArgs()
                .WithBucket(bucket)
                .WithObject(storagePath)
                .WithCallbackStream(stream => stream.CopyTo(pdf)), cancellationToken);
        }

        var imagePattern = Path.Combine(workingDirectory, "page-%03d.png");
        await RunProcessAsync("gs", [
            "-dSAFER", "-dBATCH", "-dNOPAUSE", "-sDEVICE=png16m", "-r200",
            $"-sOutputFile={imagePattern}", pdfPath
        ], cancellationToken);

        var pages = Directory.GetFiles(workingDirectory, "page-*.png").OrderBy(path => path, StringComparer.Ordinal);
        var recognizedPages = new List<string>();
        foreach (var page in pages)
        {
            var result = await RunProcessAsync("tesseract", [page, "stdout", "-l", "eng+deu"], cancellationToken);
            recognizedPages.Add(result.Trim());
        }

        if (recognizedPages.Count == 0)
            throw new InvalidDataException("Ghostscript did not render any pages from the PDF.");
        return string.Join(Environment.NewLine, recognizedPages);
    }
    finally
    {
        Directory.Delete(workingDirectory, recursive: true);
    }
}

// Run OCR/rendering tools without a shell and return their captured standard output.
static async Task<string> RunProcessAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
{
    var startInfo = new ProcessStartInfo(executable)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {executable}.");
    var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
    await process.WaitForExitAsync(cancellationToken);
    var output = await standardOutput;
    var error = await standardError;
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{executable} exited with code {process.ExitCode}: {error}");
    return output;
}

internal sealed record UploadMessage(Guid DocumentId, string FileName, string? Description, string? StoragePath);
internal sealed record OcrCompletedMessage(Guid DocumentId, string FileName, string? Description, string TextContent);
