using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Paperless.Api.Queues;

// ############################
// Publishes document-processing events to the background worker queues.
// ############################
public interface IRabbitMqDocumentMessagePublisher
{
    void PublishDocumentUploaded(Guid documentId, string fileName, string? description = null, string? storagePath = null);
    void PublishGenAiRequest(Guid documentId, string fileName, string text, string prompt);
}

// Owns the RabbitMQ connection/channel and serializes the shared worker messages.
public sealed class RabbitMqDocumentMessagePublisher : IRabbitMqDocumentMessagePublisher, IDisposable
{
    private const string DocumentUploadedQueue = "document-uploaded";
    private const string GenAiRequestQueue = "document-genai-request";
    private const string GenAiQueueName = "document-genai-request";

    private readonly ILogger<RabbitMqDocumentMessagePublisher> _logger;
    private readonly IConnection? _connection;
    private readonly IChannel? _channel;

    public RabbitMqDocumentMessagePublisher(IConfiguration configuration, ILogger<RabbitMqDocumentMessagePublisher> logger)
    {
        _logger = logger;

        var host = configuration["RabbitMQ:Host"] ?? configuration["RabbitMq:Host"] ?? "localhost";
        var portText = configuration["RabbitMQ:Port"] ?? "5672";
        var user = configuration["RabbitMQ:User"] ?? "guest";
        var password = configuration["RabbitMQ:Password"] ?? "guest";

        if (!int.TryParse(portText, out var port))
        {
            port = 5672;
        }

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = host,
                Port = port,
                UserName = user,
                Password = password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
            _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();
            _channel.QueueDeclareAsync(DocumentUploadedQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, passive: false, cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
            _channel.QueueDeclareAsync(GenAiRequestQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, passive: false, cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
            _logger.LogInformation("RabbitMQ connected to {Host}:{Port}; queues {DocumentUploadedQueue}, {GenAiRequestQueue} ready.", host, port, DocumentUploadedQueue, GenAiRequestQueue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ unavailable at {Host}:{Port}. Document upload notifications will be skipped.", host, port);
        }
    }

    // Start OCR processing for a newly uploaded file.
    public void PublishDocumentUploaded(Guid documentId, string fileName, string? description = null, string? storagePath = null)
    {
        if (_channel is null)
        {
            _logger.LogWarning("Skipping RabbitMQ publish for document {DocumentId} because the connection is unavailable.", documentId);
            return;
        }

        try
        {
            var payload = new
            {
                documentId,
                fileName,
                description,
                storagePath,
                eventType = "document.uploaded",
                createdAtUtc = DateTime.UtcNow
            };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            var properties = new BasicProperties { Persistent = true };

            _channel.BasicPublishAsync(string.Empty, DocumentUploadedQueue, false, properties, body, CancellationToken.None).GetAwaiter().GetResult();
            _logger.LogInformation("Published document upload event for {DocumentId} to RabbitMQ queue {QueueName}.", documentId, DocumentUploadedQueue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish document upload event for {DocumentId}.", documentId);
        }
    }

    // Send recognized text and optional per-request instructions to the GenAI worker.
    public void PublishGenAiRequest(Guid documentId, string fileName, string text, string prompt)
    {
        if (_channel is null)
        {
            _logger.LogWarning("Skipping RabbitMQ publish for GenAI request of document {DocumentId} because the connection is unavailable.", documentId);
            return;
        }

        try
        {
            var payload = new
            {
                documentId,
                fileName,
                text,
                prompt,
                eventType = "genai.request",
                createdAtUtc = DateTime.UtcNow
            };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            var properties = new BasicProperties { Persistent = true };

            _channel.BasicPublishAsync(string.Empty, GenAiRequestQueue, false, properties, body, CancellationToken.None).GetAwaiter().GetResult();
            _logger.LogInformation("Published GenAI request for document {DocumentId} to RabbitMQ queue {QueueName}.", documentId, GenAiRequestQueue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish GenAI request for document {DocumentId}.", documentId);
        }
    }

    public void Dispose()
    {
        _channel?.CloseAsync().GetAwaiter().GetResult();
        _connection?.CloseAsync().GetAwaiter().GetResult();
    }
}
