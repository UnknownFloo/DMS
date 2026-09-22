using Microsoft.EntityFrameworkCore;
using Paperless.Api.Entities;

namespace Paperless.Api.Data;

public class PaperlessDbContext(DbContextOptions<PaperlessDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionDocument> CollectionDocuments => Set<CollectionDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<DocumentTag>(entity =>
        {
            entity.HasKey(x => new { x.DocumentId, x.TagId });
            entity.HasOne(x => x.Document).WithMany(x => x.DocumentTags).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Tag).WithMany(x => x.Documents).HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<CollectionDocument>(entity =>
        {
            entity.HasKey(x => new { x.CollectionId, x.DocumentId });
            entity.HasOne(x => x.Collection).WithMany(x => x.Documents).HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Document).WithMany(x => x.Collections).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
