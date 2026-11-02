using System.Net.Http.Json;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

const string queueName = "document-ocr-completed";
var rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
var rabbitPort = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var parsedPort) ? parsedPort : 5672;
var rabbitUser = Environment.GetEnvironmentVariable("RABBITMQ_USERNAME") ?? "guest";
var rabbitPassword = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest";
var elasticsearchUrl = Environment.GetEnvironmentVariable("ELASTICSEARCH_URL") ?? "http://localhost:9200";

var factory = new ConnectionFactory
{
    HostName = rabbitHost,
    Port = rabbitPort,
    UserName = rabbitUser,
    Password = rabbitPassword,
    AutomaticRecoveryEnabled = true
};
using var connection = await factory.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync();
using var httpClient = new HttpClient { BaseAddress = new Uri(elasticsearchUrl) };

await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
await channel.BasicQosAsync(0, 1, false);
Console.WriteLine($"Indexer listening on RabbitMQ {rabbitHost}:{rabbitPort} / queue {queueName}; Elasticsearch {elasticsearchUrl}");

var consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += async (_, eventArgs) =>
{
    try
    {
        var message = JsonSerializer.Deserialize<OcrCompletedMessage>(eventArgs.Body.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Empty OCR result message.");
        var document = new
        {
            documentId = message.DocumentId,
            fileName = message.FileName,
            description = message.Description,
            textContent = message.TextContent,
            indexedAtUtc = DateTime.UtcNow
        };
        using var response = await httpClient.PutAsJsonAsync($"/paperless-documents/_doc/{message.DocumentId:D}", document);
        response.EnsureSuccessStatusCode();
        await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false);
        Console.WriteLine($"[indexer] Indexed {message.FileName} ({message.TextContent.Length} characters).");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"[indexer] Failed to index OCR result: {exception}");
        await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true);
    }
};

await channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer);
Console.WriteLine("Indexer started. Press Ctrl+C to stop.");
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.Cancel();
};
await Task.Delay(Timeout.Infinite, stopping.Token);

internal sealed record OcrCompletedMessage(Guid DocumentId, string FileName, string? Description, string TextContent);