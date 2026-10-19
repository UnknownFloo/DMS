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
- .NET SDK 8.0

## Credentials
- PostgreSQL: `paperless` / `paperless`
- RabbitMQ: `guest` / `guest`

## Final sprint validation
Successful Sprint 3 validation is:
- app builds without errors
- Docker Compose starts all services
- frontend is reachable via http://localhost/
- API is reachable and health checks pass
- uploading a document produces a queue message visible in the worker log
