# Paperless DMS

Project overview for the SWEN3 semester project. The project is split into sprint-based milestones and follows the required architecture of REST API + database + web frontend + message queue.

## Sprint 1 – Project setup, REST API, DAL

### Goal
- .NET/C# project setup
- REST API with code-first endpoints
- PostgreSQL persistence with Entity Framework Core
- Repository pattern
- Unit tests with mocked repository layer
- Docker Compose for API + database
- Additional use case: Collections

### Architecture
`Controller -> Service -> Repository -> EF Core DbContext -> PostgreSQL`

### Main entities
- `Document`
- `Tag`
- `DocumentTag`
- `Collection`
- `CollectionDocument`

### Run Sprint 1 locally
```bash
cd /home/roberto/Downloads/SWEN_3/DMS
dotnet restore Paperless.sln
dotnet build Paperless.sln -nologo
dotnet test Paperless.sln -nologo
```

### Docker for Sprint 1
```bash
docker compose build
docker compose up -d
```

### Sprint 1 endpoints
- `GET /api/documents`
- `GET /api/documents/{id}`
- `POST /api/documents`
- `PUT /api/documents/{id}`
- `DELETE /api/documents/{id}`

### Additional use case: Collections
- `GET /api/collections`
- `GET /api/collections/{id}`
- `POST /api/collections`
- `POST /api/collections/{id}/documents`

---

## Sprint 2 – Web UI

### Goal
- Web server service (Nginx)
- UI served from static frontend files
- Dashboard and detail pages served by the webserver
- Frontend communication with REST API
- Docker Compose extended with UI container

### Frontend
The frontend is served from the `frontend/` directory through the `ui` container.

### Start Sprint 2 stack
```bash
docker compose up -d --build
```

### Frontend URLs
- Frontend: http://localhost/
- API Swagger: http://localhost:8080/swagger
- API health: http://localhost:8080/health

### Useful checks
```bash
curl -f http://localhost/
curl -f http://localhost/api/health
```

### Notes
- The frontend uses relative API requests (`/api`) so it can work through the Nginx proxy.
- Static files are served by Nginx and cached with header settings in `nginx.conf`.

---

## Sprint 3 – RabbitMQ / queue integration

### Goal
- Extend Docker Compose with RabbitMQ
- Integrate queue publishing into REST API
- Uploading a document triggers a message to RabbitMQ
- Empty OCR worker consumes the queue and logs the message
- Exception handling and logging added

### Services in Sprint 3
- PostgreSQL
- API
- UI
- RabbitMQ
- OCR worker

### Start Sprint 3 stack
```bash
cd /home/roberto/Downloads/SWEN_3/DMS
docker compose up -d --build
```

### Confirm queue flow
1. Open the frontend at http://localhost/
2. Upload a PDF document
3. Check logs:

```bash
docker compose logs -f worker
```

Expected output example:
```text
[worker] OCR message received: {"documentId":"...","fileName":"...pdf","eventType":"document.uploaded"}
```

This confirms:
- upload succeeded in the frontend/API
- API published message to RabbitMQ
- worker consumed the message from the queue

### RabbitMQ access
- RabbitMQ UI: http://localhost:15672
- Username: `guest`
- Password: `guest`

### Worker queue name
- `document-uploaded`

---

## Sprint 4 – Silo, OCR, Elasticsearch

### Processing flow
- PDF uploads are stored in the Silo object store through its S3-compatible API.
- RabbitMQ's `document-uploaded` queue carries the document ID and Silo object key.
- The OCR worker downloads each PDF, renders every page with Ghostscript, and recognizes German/English text with Tesseract.
- OCR results are published to `document-ocr-completed` and written idempotently to Elasticsearch.
- `GET /api/documents/search?q=Hello` searches file names, descriptions, and recognized text.
- The home page at http://localhost/ and the Documents page use that search endpoint.

### Services and local URLs
- Paperless UI: http://localhost/
- API Swagger: http://localhost:8080/swagger
- Silo S3 API: http://localhost:9000
- Silo console: http://localhost:9001 (user `paperless`, password `paperless-secret-key`)
- RabbitMQ management: http://localhost:15672 (user/password `guest`)
- Elasticsearch: http://localhost:9200
- Kibana: http://localhost:5601
- Logstash TCP input: localhost:5044

### Start and check the full stack
```bash
docker compose build
docker compose up -d
docker compose ps
```

Upload `HelloWorld.pdf` through the Upload page at http://localhost/. Wait for the worker and indexer to finish, then search for `Hello` in the home-page search field. The PDF should appear in the results. Processing can be followed with:

```bash
docker compose logs -f worker indexer
```

The OCR worker requires a PDF with a readable scan or existing text layer. Search results are available after the indexing worker processes the OCR event.

### Storage and cloud requirements
This project uses the `pgsty/silo` container image, not the removed MinIO image. Silo exposes a local S3-compatible endpoint; the MinIO-compatible NuGet client is only used as an S3 protocol client. All credentials and data are local to Docker Compose. No AWS account, AWS SDK, or AWS service is used.

### Data persistence
PostgreSQL, Silo, and Elasticsearch data are stored in named Docker volumes. To remove the data as well as containers, run `docker compose down -v`.

---

## Project run commands

### Build and test
```bash
dotnet restore Paperless.sln
dotnet build Paperless.sln -nologo
dotnet test Paperless.sln -nologo
```

### Docker lifecycle
```bash
docker compose up -d --build
docker compose ps
docker compose logs -f
docker compose down
```

### Check PostgreSQL data
```bash
docker compose exec postgres psql -U paperless -d paperless -c "select * from \"Documents\";"
```

---

## Git commands

Check status:
```bash
git status
```

Stage all changes:
```bash
git add .
```

Commit:
```bash
git commit -m "Sprint 3: RabbitMQ queue integration and worker"
```

Push:
```bash
git push
```

If the branch is new:
```bash
git push --set-upstream origin <branch-name>
```

---

## Requirements
- Docker
- Docker Compose
- .NET SDK 10.0.100 for local builds (`global.json`); the applications target .NET 8

## Credentials
- PostgreSQL: `paperless` / `paperless`
- RabbitMQ: `guest` / `guest`
- Silo: `paperless` / `paperless-secret-key` (local development only)

## Final sprint validation
Successful Sprint 4 validation is:
- app builds without errors
- `docker compose build` completes without errors
- `docker compose up -d` starts all required containers
- frontend is reachable via http://localhost/
- API is reachable and health checks pass
- PDF uploads are stored in Silo
- OCR and indexing workers complete their queue messages
- searching for `Hello` after uploading `HelloWorld.pdf` returns that document
