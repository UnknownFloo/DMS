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

## Sprint 2 — Additions / Notes

- UI served from `frontend/` by the `ui` (Nginx) service defined in `docker-compose.yml`.
- CSS was split into `css/common.css` + per-page files (`index.css`, `upload.css`, `collections.css`, `documents.css`, `document.css`). A lightweight compatibility shim `css/style.css` remains for older deployments.
- API client: `frontend/js/api-client.js` uses a relative `/api` base so the frontend works behind the containerized Nginx proxy.
- PDF preview: an embedded PDF.js viewer was added at `frontend/vendor/pdfjs/viewer.html`. Document preview frames now point to that viewer for consistent in‑app rendering (no reliance on browser extensions).
- Nginx proxy tweaks:
  - `/api/health` is forwarded to the API's `/health` endpoint for Compose health checks.
  - `/api/` requests are proxied to the API controllers (e.g. `/api/collections`).
  - Note: static assets are configured with long `expires` headers (1y). For development, either clear your browser cache, enable DevTools → Disable cache, or temporarily change `nginx.conf` to reduce expires.

Troubleshooting / common commands

1) Rebuild UI after frontend changes so the Nginx image contains the updated files:

```bash
docker compose build ui
docker compose up -d ui
```

2) Full rebuild (API + UI + DB):

```bash
docker compose build
docker compose up -d
```

3) Quick checks:

```bash
curl -f http://localhost/           # frontend index
curl -f http://localhost/api/health # API health via nginx proxy
curl -I http://localhost/css/common.css
curl -I http://localhost/vendor/pdfjs/viewer.html
```

If the viewer script from a CDN is blocked due to MIME or `X-Content-Type-Options: nosniff`, add the local `pdf.min.js`/`pdf.worker.min.js` into `frontend/vendor/pdfjs/` (I can add these locally if you prefer an offline copy).

If everything above returns the expected responses, the Sprint‑2 MUST‑HAVEs are satisfied: UI served by a webserver container, pages served from the server, frontend communicates with the REST API, and Docker Compose runs the UI container.

## Frontend / UI notes (Sprint 2)

- The frontend is served from the `frontend/` folder by the `ui` service (Nginx) defined in `docker-compose.yml`.
- During development the frontend uses a relative API path (`/api`) so requests are proxied through Nginx to the API container. This avoids hardcoding `localhost:8080` and works both in containers and when serving files from the host.
- Nginx config (`nginx.conf`) sets long caching for static assets (`expires 1y`). When developing CSS/JS changes either:
  - disable the cache headers in `nginx.conf`, or
  - append a cache-busting query string to assets (e.g. `css/style.css?v=2`) while testing, or
  - open DevTools → Network and check "Disable cache" while DevTools is open.

Quick verification after starting Docker Compose:

```bash
# build and start
docker compose build
docker compose up -d

# frontend should be reachable
curl -f http://localhost/ | head -n 5

# API health
curl -f http://localhost/api/health
```

If `http://localhost/` returns the frontend HTML and `http://localhost/api/health` is OK, the Sprint-2 web UI integration is functioning.

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
