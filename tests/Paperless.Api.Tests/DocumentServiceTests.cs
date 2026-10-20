using Moq;
using Paperless.Api.DTOs;
using Paperless.Api.Entities;
using Paperless.Api.Repositories;
using Paperless.Api.Services;
using Xunit;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace Paperless.Api.Tests;

public class DocumentServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesDocument_WithoutProductionDatabase()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

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
    public async Task GetAllAsync_ReturnsMappedDocuments()
    {
        var repo = new Mock<IDocumentRepository>();
        var docId = Guid.NewGuid();
        repo.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new Document
            {
                Id = docId,
                FileName = "report.pdf",
                ContentType = "application/pdf",
                StoragePath = "/tmp/report.pdf",
                Description = "Quarterly report",
                UploadedAtUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc),
                DocumentTags =
                [
                    new DocumentTag { Tag = new Tag { Name = "finance" } },
                    new DocumentTag { Tag = new Tag { Name = "report" } }
                ]
            }
        ]);

        var service = new DocumentService(repo.Object);

        var result = await service.GetAllAsync(CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal("report.pdf", item.FileName);
        Assert.Equal("Quarterly report", item.Description);
        Assert.Equal(["finance", "report"], item.Tags.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task UpdateAsync_UpdatesExistingDocument()
    {
        var repo = new Mock<IDocumentRepository>();
        var id = Guid.NewGuid();
        repo.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new Document
        {
            Id = id,
            FileName = "old.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/old.pdf",
            Description = "Old description",
            UploadedAtUtc = DateTime.UtcNow,
            DocumentTags = []
        });
        repo.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var service = new DocumentService(repo.Object);

        var result = await service.UpdateAsync(id,
            new UpdateDocumentRequest("  new-report.pdf  ", "Updated description"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("new-report.pdf", result.FileName);
        Assert.Equal("Updated description", result.Description);
        repo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_RemovesDocumentAndFile_WhenExists()
    {
        var repo = new Mock<IDocumentRepository>();
        var id = Guid.NewGuid();
        var filePath = Path.Combine(Path.GetTempPath(), $"doc-{Guid.NewGuid():N}.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "content");

        var document = new Document
        {
            Id = id,
            FileName = "delete.pdf",
            StoragePath = filePath,
            ContentType = "application/pdf",
            UploadedAtUtc = DateTime.UtcNow
        };

        repo.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        repo.Setup(x => x.DeleteAsync(document, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var service = new DocumentService(repo.Object);

        var result = await service.DeleteAsync(id, CancellationToken.None);

        Assert.True(result);
        Assert.False(File.Exists(filePath));
        repo.Verify(x => x.DeleteAsync(document, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadFileAsync_CreatesDocumentAndFileOnDisk()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var root = Path.Combine(Path.GetTempPath(), $"paperless-{Guid.NewGuid():N}");
        var service = new DocumentService(repo.Object, new DocumentStorageOptions(root));

        await using var stream = new MemoryStream("hello"u8.ToArray());
        var file = new FormFile(stream, 0, stream.Length, "file", "invoice.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var result = await service.UploadFileAsync(file, "Uploaded from test", CancellationToken.None);

        Assert.Equal("invoice.pdf", result.FileName);
        Assert.Equal("Uploaded from test", result.Description);
        Assert.True(File.Exists(result.StoragePath));
        repo.Verify(x => x.AddAsync(It.Is<Document>(d => d.FileName == "invoice.pdf"), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task CreateAsync_RejectsMissingFileName()
    {
        var repo = new Mock<IDocumentRepository>();
        var service = new DocumentService(repo.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            new CreateDocumentRequest(" ", null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_TrimsAndDeduplicatesTags()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var service = new DocumentService(repo.Object);

        var result = await service.CreateAsync(
            new CreateDocumentRequest("  budget.pdf  ", "application/pdf", "Budget", ["Finance", "finance", "  HR  ", "hr"]), CancellationToken.None);

        Assert.Equal("budget.pdf", result.FileName);
        Assert.Equal(["Finance", "HR"], result.Tags.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task CreateAsync_UsesDefaultContentType_WhenNull()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var service = new DocumentService(repo.Object);

        var result = await service.CreateAsync(new CreateDocumentRequest("report.pdf", null, "desc", null), CancellationToken.None);

        Assert.Equal("application/pdf", result.ContentType);
    }

    [Fact]
    public async Task CreateAsync_PublishesRabbitMqEvent_WhenPublisherProvided()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var publisher = new Mock<IRabbitMqDocumentMessagePublisher>();
        var service = new DocumentService(repo.Object, messagePublisher: publisher.Object);

        var result = await service.CreateAsync(new CreateDocumentRequest("hello.pdf", "application/pdf", "hello", null), CancellationToken.None);

        publisher.Verify(x => x.PublishDocumentUploaded(result.Id, result.FileName, result.Description), Times.Once);
    }

    [Fact]
    public async Task GetAsync_ReturnsDocument_WhenExists()
    {
        var repo = new Mock<IDocumentRepository>();
        var id = Guid.NewGuid();
        repo.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new Document
        {
            Id = id,
            FileName = "notes.txt",
            ContentType = "text/plain",
            StoragePath = "/tmp/notes.txt",
            Description = "some notes",
            UploadedAtUtc = DateTime.UtcNow,
            DocumentTags = [new DocumentTag { Tag = new Tag { Name = "work" } }]
        });

        var service = new DocumentService(repo.Object);

        var result = await service.GetAsync(id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("notes.txt", result!.FileName);
        Assert.Equal(["work"], result.Tags.ToArray());
    }

    [Fact]
    public async Task GetDocumentForFileAsync_DelegatesToRepository()
    {
        var repo = new Mock<IDocumentRepository>();
        var id = Guid.NewGuid();
        var document = new Document { Id = id, FileName = "file.pdf" };
        repo.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(document);

        var service = new DocumentService(repo.Object);

        var result = await service.GetDocumentForFileAsync(id, CancellationToken.None);

        Assert.Same(document, result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoDocuments()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Document>());

        var service = new DocumentService(repo.Object);

        var result = await service.GetAllAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenDocumentMissing()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Document?)null);

        var service = new DocumentService(repo.Object);

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateDocumentRequest("x.pdf", "desc"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_TrimsValuesAndSetsUpdatedAt()
    {
        var repo = new Mock<IDocumentRepository>();
        var id = Guid.NewGuid();
        var document = new Document
        {
            Id = id,
            FileName = "old.pdf",
            ContentType = "application/pdf",
            StoragePath = "/tmp/old.pdf",
            Description = "old",
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1),
            DocumentTags = []
        };
        repo.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        repo.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var service = new DocumentService(repo.Object);

        var result = await service.UpdateAsync(id, new UpdateDocumentRequest("  updated.pdf  ", "  better description  "), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("updated.pdf", document.FileName);
        Assert.Equal("better description", document.Description);
        Assert.NotNull(document.UpdatedAtUtc);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenDocumentMissing()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Document?)null);

        var service = new DocumentService(repo.Object);

        var result = await service.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_DeletesEntityEvenWithoutFile()
    {
        var repo = new Mock<IDocumentRepository>();
        var id = Guid.NewGuid();
        var document = new Document
        {
            Id = id,
            FileName = "gone.pdf",
            StoragePath = "/tmp/does-not-exist.pdf",
            ContentType = "application/pdf",
            UploadedAtUtc = DateTime.UtcNow
        };
        repo.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        repo.Setup(x => x.DeleteAsync(document, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var service = new DocumentService(repo.Object);

        var result = await service.DeleteAsync(id, CancellationToken.None);

        Assert.True(result);
        repo.Verify(x => x.DeleteAsync(document, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadFileAsync_RejectsNullFile()
    {
        var repo = new Mock<IDocumentRepository>();
        var service = new DocumentService(repo.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadFileAsync(null!, "desc", CancellationToken.None));
    }

    [Fact]
    public async Task UploadFileAsync_RejectsEmptyFile()
    {
        var repo = new Mock<IDocumentRepository>();
        var service = new DocumentService(repo.Object);

        await using var stream = new MemoryStream();
        var file = new FormFile(stream, 0, 0, "file", "empty.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadFileAsync(file, "desc", CancellationToken.None));
    }

    [Fact]
    public async Task UploadFileAsync_PublishesRabbitMqEvent_WhenPublisherProvided()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(x => x.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var publisher = new Mock<IRabbitMqDocumentMessagePublisher>();
        var root = Path.Combine(Path.GetTempPath(), $"paperless-upload-{Guid.NewGuid():N}");
        var service = new DocumentService(repo.Object, new DocumentStorageOptions(root), publisher.Object);

        await using var stream = new MemoryStream("data"u8.ToArray());
        var file = new FormFile(stream, 0, stream.Length, "file", "upload.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var result = await service.UploadFileAsync(file, "uploading", CancellationToken.None);

        publisher.Verify(x => x.PublishDocumentUploaded(result.Id, result.FileName, result.Description), Times.Once);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task CreateAsync_RejectsFileNameLongerThan255Chars()
    {
        var repo = new Mock<IDocumentRepository>();
        var service = new DocumentService(repo.Object);
        var longName = new string('a', 256);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateDocumentRequest(longName, "application/pdf", "desc", null), CancellationToken.None));
    }
}
