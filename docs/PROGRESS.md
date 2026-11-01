# LinklY - Project Progress

## Current Status: Phase B Completed

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
  - `redis:7-alpine` added to Docker Compose with 128MB maxmemory, `allkeys-lru`, and healthcheck.
  - `ILinkCache` abstraction in Domain, `RedisLinkCache` and `NoOpLinkCache` in Infrastructure.
  - `Caching:Enabled` configuration flag to benchmark with cache toggled on/off.
  - JSON serialization of `{ url, expiresAtUtc, isNotFound }` with TTL = `min(24h, remaining time)`.
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

### What is Next
- **Phase C: Rate Limiting**:
  - Built-in ASP.NET Core RateLimiter middleware.
  - `POST /api/links`: 20 req/min per IP (sliding window), 429 ProblemDetails with `Retry-After`.
  - `GET /{code}`: configurable rate limiting via appsettings.
  - `ForwardedHeadersOptions` reverse proxy configuration and documentation of IP spoofing risks.
  - ADR 0005 covering in-memory vs. distributed rate limiter trade-offs.

### Known Gaps
- Benchmark harness updates for 80/20 hot set distribution (Phase D).
- Architecture diagram in main README (Phase D).
