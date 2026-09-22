using Moq;
using Paperless.Api.DTOs;
using Paperless.Api.Entities;
using Paperless.Api.Repositories;
using Paperless.Api.Services;

namespace Paperless.Api.Tests;

public class DocumentServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesDocument_WithoutProductionDatabase()
    {
        var repo = new Mock<IDocumentRepository>();
        var service = new DocumentService(repo.Object);

        var result = await service.CreateAsync(
            new CreateDocumentRequest("invoice.pdf", "application/pdf", "March invoice", ["invoice", "finance"]), CancellationToken.None);

        Assert.Equal("invoice.pdf", result.FileName);
        Assert.Equal(2, result.Tags.Count);
        repo.Verify(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenDocumentDoesNotExist()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Document?)null);
        var service = new DocumentService(repo.Object);

        var result = await service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_RejectsMissingFileName()
    {
        var repo = new Mock<IDocumentRepository>();
        var service = new DocumentService(repo.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            new CreateDocumentRequest(" ", null, null, null), CancellationToken.None));
    }
}
