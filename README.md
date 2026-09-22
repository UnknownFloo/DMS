# Paperless – Sprint 1 (C# / .NET 10)

## Sprint-1 scope
- ASP.NET Core REST server
- PostgreSQL persistence with Entity Framework Core / Npgsql
- Repository pattern and entity mapping
- Unit tests with Moq; production database is mocked out
- Docker Compose for REST API + PostgreSQL
- Additional use case: **Collections** (create a collection and assign documents to it)

## Architecture
`Controller -> Service -> Repository -> EF Core DbContext -> PostgreSQL`

Entities:
- `Document`
- `Tag`
- `DocumentTag`
- `Collection` (additional use case)
- `CollectionDocument` (additional use case)

## Run with Docker

```bash
docker compose build
docker compose up
```

API: http://localhost:8080
Swagger: http://localhost:8080/swagger
Health: http://localhost:8080/health

The API creates the database schema automatically on startup (`EnsureCreated`) for the Sprint-1 setup.

## REST endpoints

### Documents
- `GET /api/documents`
- `GET /api/documents/{id}`
- `POST /api/documents`
- `PUT /api/documents/{id}`
- `DELETE /api/documents/{id}`

Example:
```json
{
  "fileName": "invoice-001.pdf",
  "contentType": "application/pdf",
  "description": "March invoice",
  "tags": ["invoice", "finance"]
}
```

### Additional use case: Collections
- `POST /api/collections`
- `GET /api/collections/{id}`
- `POST /api/collections/{id}/documents`

Example:
```json
{
  "name": "Taxes 2026",
  "description": "Tax-related documents"
}
```

Then assign a document:
```json
{
  "documentId": "<document-guid>"
}
```

## Unit tests

```bash
dotnet test Paperless.sln
```
