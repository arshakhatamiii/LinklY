# LinklY - Project Progress

## Current Status: All Phases Completed (Definition of Done Met)

### What is Done
- **Foundation & Core Shortener (.NET 10 Minimal APIs)**:
  - Base62 encoding from PostgreSQL sequence (`PostgresSequenceShortCodeGenerator`).
  - Validation: RFC absolute URLs, custom alias regex (`[A-Za-z0-9_-]{3,30}`), reserved words rejection.
  - Concurrency safety: PostgreSQL unique index handling `23505` returning `409 Conflict`.
  - Expiration: `TimeProvider` injection, read-time expiry check returning `410 Gone`.
  - Multi-stage Dockerfile and Docker Compose with healthchecks.
  - GitHub Actions CI (`.github/workflows/ci.yml`) running Testcontainers tests.
  - ADRs: `0001-code-generation.md`, `0002-alias-and-expiry.md`.
- **Phase A: Redis Cache-Aside & Stampede Protection**:
  - `redis:7-alpine` in Docker Compose with 128MB maxmemory, `allkeys-lru`, and healthcheck.
  - `ILinkCache` abstraction in Domain, `RedisLinkCache` and `NoOpLinkCache` in Infrastructure.
  - `Caching:Enabled` configuration flag to benchmark with cache toggled on/off.
  - JSON serialization of `{ id, url, expiresAtUtc, isNotFound }` with TTL = `min(24h, remaining time)`.
  - Negative caching: 60s TTL for non-existent codes, invalidated on link/alias creation.
  - `ISingleFlight` in-process deduplication preventing cache stampedes.
  - Resilience: 50ms async timeout, 5s backoff circuit on failure, fail-open fallback to PostgreSQL.
  - Observability: `CacheMetrics` counters (`cache_hits`, `cache_misses`, `cache_errors`) exposed at `/internal/stats`.
  - ADR: `0003-redis-cache.md`.
  - Tests: Testcontainers Redis integration tests verifying hits, misses, negative caching, alias cache invalidation, expired link 410, single-flight deduplication, and Redis failure fallback.
- **Phase B: Async Click Analytics**:
  - `ClickEvent` entity & EF Core migration (`click_events` table indexed on `(LinkId, ClickedAtUtc)`). Truncated Referrer and UserAgent (max 200 chars). Privacy-preserving: zero raw IP storage.
  - `ClickAnalyticsChannel` with bounded `Channel<ClickEvent>` (capacity 10,000, `DropOldest`, dropped click counter).
  - Non-blocking writes directly in `RedirectToLink` without database await on both cache hits and misses.
  - `ClickAnalyticsBatchProcessor` background worker flushing batches at 500 events or every 2 seconds, with graceful shutdown flush draining.
  - Endpoint `GET /api/links/{code}/stats` returning `totalClicks` and chronological `clicksPerDay` for the last 30 days.
  - ADR: `0004-async-click-analytics.md`.
  - Tests: Batch persistence, stats endpoint calculation and 404 handling, channel overflow dropping oldest and incrementing metric, and graceful shutdown flushing.
- **Phase C: Rate Limiting**:
  - ASP.NET Core built-in `RateLimiter` middleware (`Microsoft.AspNetCore.RateLimiting`).
  - `POST /api/links`: Sliding window limiter (20 requests / 1 minute per client IP, 6 segments, queue limit 0).
  - 429 response handling: Returns RFC 7807 `ProblemDetails` and standard `Retry-After` header.
  - `GET /{code}`: Unthrottled by default, optionally configurable via `RateLimiting:Redirect:Enabled`.
  - `ForwardedHeaders` configured for reverse proxy IP resolution (`X-Forwarded-For`, `X-Forwarded-Proto`) and spoofing documentation.
  - ADR: `0005-rate-limiting.md`.
  - Tests: 21st request returns 429 with `Retry-After`, different IPs are isolated, redirect hot path unaffected by POST rate limits.
- **Phase D: Benchmark Harness & Documentation**:
  - Updated `loadtest/redirect.js` supporting both `uniform` and `hotset` (Pareto 80/20) traffic distributions over N=5000 seeded links with `ramping-arrival-rate`.
  - Updated `loadtest/README.md` with cache on/off docker instructions, `dropped_iterations` guide, and results table with `TODO` placeholders.
  - Updated main `README.md` with full mermaid architecture diagram, feature list, single-command run instructions, all ADR links (0001-0005), design trade-offs, and known limitations.
  - CI pipeline verified green for all 62 unit and integration tests.

### Definition of Done Checklist
- [x] `docker compose up` works cleanly with single command locally.
- [x] All 62 unit and integration tests green (`dotnet test`).
- [x] CI workflow runs test suite on push/PR.
- [x] ADRs 0001 through 0005 all exist in `/docs/adr/`.
- [x] `docs/PROGRESS.md` reflects final completed state.
- [x] No invented benchmark numbers (tables have `TODO` placeholders).
