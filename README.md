# URL Shortener (.NET 10 Minimal APIs)

A modern, high-performance URL shortening service foundation built with **.NET 10**, **ASP.NET Core Minimal APIs**, **PostgreSQL**, and **Entity Framework Core**.

---

## Features

- **Minimal APIs Architecture**: Clean separation into `Domain`, `Infrastructure`, `Api`, and `Tests`.
- **Collision-Free Code Generation**: PostgreSQL sequence coupled with an isolated Base62 encoder.
- **Robust Validation**: Validates absolute HTTP and HTTPS URLs according to RFC standards.
- **Observability & Reliability**:
  - Structured logging with **Serilog**.
  - RFC 7807 compliant **ProblemDetails** for error responses.
  - Native **Health Checks** endpoint for orchestrators.
- **Zero-Setup Testing**: Integration tests powered by **Testcontainers** (spins up a disposable PostgreSQL container automatically).
- **Turnkey Containerization**: Multi-stage `Dockerfile` and `docker-compose.yml` for single-command local execution.

---

## Tech Stack

- **Runtime & Framework**: .NET 10, C# 13, ASP.NET Core Minimal APIs
- **Database**: PostgreSQL 17 (Npgsql + EF Core)
- **Logging**: Serilog (Console & Structured JSON)
- **Testing**: xUnit, Microsoft.AspNetCore.Mvc.Testing, Testcontainers.PostgreSql
- **Containers**: Docker & Docker Compose

---

## Quick Start (Docker Compose)

The entire application and database stack can be started with a single command:

```bash
docker compose up --build -d
```

Once running:
- **API Base URL**: `http://localhost:5000`
- **Health Check**: `http://localhost:5000/health`
- **PostgreSQL**: `localhost:5433` (database: `urlshortener`, user: `postgres`, password: `postgres`)

To stop the containers and remove volumes:
```bash
docker compose down -v
```

---

## API Documentation

### 1. Create Short Link
Creates a new short URL code for a valid HTTP or HTTPS destination.

- **URL**: `POST /api/links`
- **Headers**: `Content-Type: application/json`
- **Request Body**:
  ```json
  {
    "url": "https://github.com/dotnet/aspnetcore"
  }
  ```
- **Success Response** (`201 Created`):
  - **Header**: `Location: /4c92`
  - **Body**:
    ```json
    {
      "code": "4c92",
      "shortUrl": "http://localhost:5000/4c92",
      "originalUrl": "https://github.com/dotnet/aspnetcore",
      "createdAtUtc": "2026-10-31T20:15:00Z"
    }
    ```
- **Error Response** (`400 Bad Request`):
  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    "title": "Invalid URL",
    "status": 400,
    "detail": "The provided URL is not a valid absolute HTTP or HTTPS URL."
  }
  ```

---

### 2. Redirect to Original Link
Redirects visitors to the target destination.

- **URL**: `GET /{code}` (e.g. `GET http://localhost:5000/4c92`)
- **Success Response**: `302 Found` with `Location: <OriginalUrl>`
- **Error Response** (`404 Not Found`):
  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
    "title": "Link not found",
    "status": 404,
    "detail": "Short link with code 'nonexistent' was not found."
  }
  ```

---

### 3. Health Check
Endpoint to monitor system and database connectivity.

- **URL**: `GET /health`
- **Response**: `200 OK` (`Healthy`)

---

## Running Locally Without Docker Compose

If you have a local PostgreSQL instance running:

1. Update the connection string in `src/UrlShortener.Api/appsettings.json`:
   ```json
   "ConnectionStrings": {
     "Database": "Host=localhost;Database=urlshortener;Username=postgres;Password=postgres"
   }
   ```
2. Run the API project:
   ```bash
   dotnet run --project src/UrlShortener.Api
   ```
   *(Database migrations apply automatically on application startup)*

---

## Running Tests

### Unit Tests
Tests the Base62 encoder algorithm and edge cases:
```bash
dotnet test tests/UrlShortener.UnitTests
```

### Integration Tests
Executes end-to-end endpoint tests against a real PostgreSQL instance started automatically using Testcontainers:
```bash
dotnet test tests/UrlShortener.IntegrationTests
```

### Run All Tests
```bash
dotnet test
```

---

## Architectural Decisions

Detailed rationale behind the code generation strategy (PostgreSQL Sequence + Base62 vs. Random String Generation) is documented in:
- [ADR 0001: Short Code Generation Strategy](docs/adr/0001-code-generation.md)
