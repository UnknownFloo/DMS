using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
var port = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var parsedPort) ? parsedPort : 5672;
var user = Environment.GetEnvironmentVariable("RABBITMQ_USERNAME") ?? "guest";
var password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest";

var factory = new ConnectionFactory { HostName = host, Port = port, UserName = user, Password = password };
using var connection = await factory.CreateConnectionAsync();
using var channel = await connection.CreateChannelAsync();

await channel.QueueDeclareAsync("document-uploaded", durable: true, exclusive: false, autoDelete: false, arguments: null, passive: false, cancellationToken: CancellationToken.None);

Console.WriteLine($"OCR worker listening on RabbitMQ {host}:{port} / queue document-uploaded");

var consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += async (_, eventArgs) =>
{
    var message = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
    Console.WriteLine($"[worker] OCR message received: {message}");
    await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false);
};

await channel.BasicConsumeAsync("document-uploaded", autoAck: false, consumer: consumer, cancellationToken: CancellationToken.None);

Console.WriteLine("Worker started. Press Ctrl+C to stop.");
await Task.Delay(Timeout.Infinite, CancellationToken.None);
