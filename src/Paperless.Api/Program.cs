using Microsoft.EntityFrameworkCore;
using Paperless.Api.Data;
using Paperless.Api.Queues;
using Paperless.Api.Repositories;
using Paperless.Api.Services;
using Paperless.Api.Storage;

// ############################
// API host startup and service composition
// ############################
// ############################
// Register the API's web pipeline, persistence, storage, messaging, and search.
// ############################
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        builder => builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=paperless;Username=paperless;Password=paperless";
builder.Services.AddDbContext<PaperlessDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<ICollectionRepository, CollectionRepository>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<CollectionService>();
builder.Services.AddSingleton<IRabbitMqDocumentMessagePublisher, RabbitMqDocumentMessagePublisher>();
builder.Services.AddSingleton<IDocumentStorage, S3DocumentStorage>();
builder.Services.AddHttpClient<ElasticsearchDocumentSearch>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Elasticsearch:Url"] ?? "http://localhost:9200"));

var uploadsFolder = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Uploads");
Directory.CreateDirectory(uploadsFolder);

builder.Services.AddSingleton(new DocumentStorageOptions(uploadsFolder));

// ############################
// Configure middleware, routes, and the initial database schema checks.
// ############################
var app = builder.Build();

app.UseCors("AllowFrontend");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaperlessDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Documents\" ADD COLUMN IF NOT EXISTS \"Summary\" text NULL;");
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Documents\" ADD COLUMN IF NOT EXISTS \"OcrText\" text NULL;");
}

app.Run();

public partial class Program { }

public sealed record DocumentStorageOptions(string RootPath);
