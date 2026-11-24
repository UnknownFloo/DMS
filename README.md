# Paperless DMS

Paperless DMS is a document management system built for the SWEN3 semester project. It combines a browser interface, a REST API, PostgreSQL metadata, S3-compatible file storage, background OCR, full-text search, and optional AI-generated summaries.

## Sprint 5

Sprint 5 completes the document editing and AI-summary workflows:

- Edit existing PDF text in the browser. Scanned pages use OCR to identify editable text regions.
- Recognize and edit text in scanned images, with recognized regions highlighted in the editor.
- Save PDF and image edits as separate copies so the uploaded original stays intact.
- Run OCR and update the search index for edited copies after saving.
- Configure Gemini or an OpenAI-compatible local provider and regenerate summaries with a selected output style and optional instructions.
- Format summaries in the document overview and ignore stale search-index hits whose document records no longer exist.
- Add clear English section comments across the application source and deployment files.

## Architecture

```text
Browser ── Nginx ── Paperless API ── PostgreSQL
                         │
                         ├── Silo object storage
                         ├── RabbitMQ ── OCR worker ── Elasticsearch
                         │                    └──────── GenAI worker
                         └── Elasticsearch search
```

The OCR worker downloads uploaded files from Silo. It renders PDF pages with Ghostscript and recognizes English and German text with Tesseract. OCR results are sent to the indexer and saved with the document record. Edited copies use the same processing flow so their current text can be searched.

## Services

| Service | Purpose | Local address |
| --- | --- | --- |
| `ui` | Static frontend served by Nginx; proxies API requests | <http://localhost/> |
| `api` | ASP.NET Core REST API | <http://localhost:8080/> |
| `postgres` | Document metadata and relationships | `localhost:5432` |
| `silo` | S3-compatible file storage | <http://localhost:9000/> |
| `rabbitmq` | Background processing queues | <http://localhost:15672/> |
| `worker` | PDF/image OCR | Internal service |
| `indexer` | Writes OCR results to Elasticsearch | Internal service |
| `elasticsearch` | Full-text search index | <http://localhost:9200/> |
| `genai-worker` | Generates summaries using the configured provider | Internal service |
| `kibana` | Elasticsearch inspection | <http://localhost:5601/> |
| `logstash` | Receives application logs | `localhost:5044` |

## Run locally

### Requirements

- Docker and Docker Compose
- .NET 8 SDK for local builds

### Start the application

From the repository root:

```bash
docker compose up -d --build
docker compose ps
```

Open <http://localhost/> to upload and manage documents. The API health check is available at <http://localhost/api/health>; Swagger is available at <http://localhost:8080/swagger>.

Upload a PDF or image and wait for OCR and indexing to finish. Search by filename, description, or recognized text. Follow background processing with:

```bash
docker compose logs -f worker indexer genai-worker
```

To stop the services while preserving data:

```bash
docker compose down
```

PostgreSQL, Silo, and Elasticsearch data use named Docker volumes. `docker compose down -v` also deletes those volumes and their data.

### Build and run the .NET projects locally

```bash
dotnet restore Paperless.sln
dotnet build Paperless.sln
dotnet test Paperless.sln
```

The OCR worker requires Ghostscript and Tesseract at runtime; Docker installs them in its image.

## Document editing and storage

Editing creates a new document record and a new stored file. It does not replace the uploaded original. In Docker, files are stored in the `paperless-documents` bucket in Silo. Browser-created copies use a `kopie_XX_` filename under the original document's storage directory. The new file is sent through OCR before it appears in text search.

The PDF editor uses PDF.js to render pages, Tesseract.js for scanned pages, and PDF-Lib to create the edited PDF. The image editor highlights OCR text regions and saves a reconstructed image copy.

## AI summaries

Open **AI Settings** from the frontend to configure one provider:

- Google Gemini
- A local or remote OpenAI-compatible endpoint, such as Ollama

Summary regeneration also supports structured overview, short summary, key points, and timeline formats, with optional user instructions. Generated Markdown is escaped and formatted by the frontend before display.

## API endpoints

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `GET` | `/api/documents` | List documents |
| `GET` | `/api/documents/{id}` | Read document metadata |
| `GET` | `/api/documents/{id}/file` | Open the stored file |
| `GET` | `/api/documents/search?q={query}` | Search indexed text and metadata |
| `POST` | `/api/documents/upload` | Upload a document and start OCR |
| `PUT` | `/api/documents/{id}` | Update document metadata |
| `DELETE` | `/api/documents/{id}` | Delete a document and its stored file |
| `POST` | `/api/documents/{id}/edit/pdf/upload` | Save a browser-edited PDF copy |
| `POST` | `/api/documents/{id}/edit/image` | Save an edited image copy |
| `GET`, `PUT` | `/api/genai/settings` | Read or update AI providers |
| `POST` | `/api/documents/{id}/summary/regenerate` | Queue a new summary |
| `GET`, `POST` | `/api/collections` | List or create collections |

## Repository layout

```text
frontend/                 Static pages, styles, and browser-side editors
src/Paperless.Api/        REST API, persistence, storage, search, and queues
src/Paperless.Worker/     OCR processing worker
src/Paperless.Indexer/    Elasticsearch indexing worker
src/Paperless.GenAI/      Summary generation worker
tests/                    API service tests
docker-compose.yml        Local application and infrastructure services
```

## Local development credentials

The Compose file uses development-only defaults:

- PostgreSQL: `paperless` / `paperless`
- RabbitMQ: `guest` / `guest`
- Silo: `paperless` / `paperless-secret-key`

Do not reuse these credentials outside a local development environment. No AWS account or AWS service is required; Silo provides the S3-compatible storage endpoint locally.
