using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Minio;
using Minio.DataModel.Args;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

const string uploadQueue = "document-uploaded";
const string ocrQueue = "document-ocr-completed";
var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
var port = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var parsedPort) ? parsedPort : 5672;
var user = Environment.GetEnvironmentVariable("RABBITMQ_USERNAME") ?? "guest";
var password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest";
var storageEndpoint = Environment.GetEnvironmentVariable("OBJECT_STORAGE_ENDPOINT") ?? "localhost:9000";
var storageAccessKey = Environment.GetEnvironmentVariable("OBJECT_STORAGE_ACCESS_KEY") ?? "paperless";
var storageSecretKey = Environment.GetEnvironmentVariable("OBJECT_STORAGE_SECRET_KEY") ?? "paperless-secret-key";
var storageBucket = Environment.GetEnvironmentVariable("OBJECT_STORAGE_BUCKET") ?? "paperless-documents";
var storageUseSsl = string.Equals(Environment.GetEnvironmentVariable("OBJECT_STORAGE_USE_SSL"), "true", StringComparison.OrdinalIgnoreCase);

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
await channel.BasicQosAsync(0, 1, false);

Console.WriteLine($"OCR worker listening on RabbitMQ {host}:{port} / queue {uploadQueue}");

var consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += async (_, eventArgs) =>
{
    try
    {
        var message = JsonSerializer.Deserialize<UploadMessage>(eventArgs.Body.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Empty upload message.");
        if (string.IsNullOrWhiteSpace(message.StoragePath))
            throw new InvalidDataException($"Upload message for {message.DocumentId} has no storage path.");

        var text = await RecognizePdfAsync(storage, storageBucket, message.StoragePath, CancellationToken.None);
        var result = new OcrCompletedMessage(message.DocumentId, message.FileName, message.Description, text);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await channel.BasicPublishAsync(string.Empty, ocrQueue, false, new BasicProperties { Persistent = true }, body, CancellationToken.None);
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
