using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Paperless.Api.Queues;

public interface IRabbitMqDocumentMessagePublisher
{
    void PublishDocumentUploaded(Guid documentId, string fileName, string? description = null);
}

public sealed class RabbitMqDocumentMessagePublisher : IRabbitMqDocumentMessagePublisher, IDisposable
{
    private const string QueueName = "document-uploaded";

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
            _channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, arguments: null, passive: false, cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
            _logger.LogInformation("RabbitMQ connected to {Host}:{Port}; queue {QueueName} ready.", host, port, QueueName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ unavailable at {Host}:{Port}. Document upload notifications will be skipped.", host, port);
        }
    }

    public void PublishDocumentUploaded(Guid documentId, string fileName, string? description = null)
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
                eventType = "document.uploaded",
                createdAtUtc = DateTime.UtcNow
            };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            var properties = new BasicProperties { Persistent = true };

            _channel.BasicPublishAsync(string.Empty, QueueName, false, properties, body, CancellationToken.None).GetAwaiter().GetResult();
            _logger.LogInformation("Published document upload event for {DocumentId} to RabbitMQ queue {QueueName}.", documentId, QueueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish document upload event for {DocumentId}.", documentId);
        }
    }

    public void Dispose()
    {
        _channel?.CloseAsync().GetAwaiter().GetResult();
        _connection?.CloseAsync().GetAwaiter().GetResult();
    }
}
