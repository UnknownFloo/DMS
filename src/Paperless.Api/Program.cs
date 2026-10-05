using Microsoft.EntityFrameworkCore;
using Paperless.Api.Data;
using Paperless.Api.Repositories;
using Paperless.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS to allow frontend to communicate with API
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

var uploadsFolder = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Uploads");
Directory.CreateDirectory(uploadsFolder);

builder.Services.AddSingleton(new DocumentStorageOptions(uploadsFolder));

var app = builder.Build();

// Enable CORS
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
}

app.Run();

public partial class Program { }

public sealed record DocumentStorageOptions(string RootPath);
