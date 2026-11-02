using Minio;
using Minio.DataModel.Args;

namespace Paperless.Api.Storage;

public sealed class S3DocumentStorage : IDocumentStorage
{
    private readonly IMinioClient _client;
    private readonly string _bucket;
    private readonly SemaphoreSlim _bucketLock = new(1, 1);

    public S3DocumentStorage(IConfiguration configuration)
    {
        var endpoint = configuration["ObjectStorage:Endpoint"] ?? "localhost:9000";
        var accessKey = configuration["ObjectStorage:AccessKey"] ?? "paperless";
        var secretKey = configuration["ObjectStorage:SecretKey"] ?? "paperless-secret-key";
        var useSsl = bool.TryParse(configuration["ObjectStorage:UseSSL"], out var parsedUseSsl) && parsedUseSsl;
        _bucket = configuration["ObjectStorage:Bucket"] ?? "paperless-documents";
        _client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(useSsl)
            .Build();
    }

    public async Task PutAsync(string key, Stream content, long size, string contentType, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(key)
            .WithStreamData(content)
            .WithObjectSize(size)
            .WithContentType(contentType), cancellationToken);
    }

    public async Task<Stream?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var content = new MemoryStream();
        await _client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(key)
            .WithCallbackStream(stream => stream.CopyTo(content)), cancellationToken);
        content.Position = 0;
        return content;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        _client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(_bucket).WithObject(key), cancellationToken);

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        await _bucketLock.WaitAsync(cancellationToken);
        try
        {
            if (!await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), cancellationToken))
            {
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), cancellationToken);
            }
        }
        finally
        {
            _bucketLock.Release();
        }
    }
}