# LinklY - Project Progress

## Current Status: Phase A Completed

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

### What is Next
- **Phase B: Async Click Analytics**:
  - `ClickEvent` entity and EF Core migration (`click_events` table indexed on `(LinkId, ClickedAtUtc)`).
  - Bounded `Channel<ClickEvent>` (capacity 10,000, `DropOldest`, dropped click counter).
  - Non-blocking writes in redirect endpoint.
  - Background worker flushing batches (500 events or 2s intervals) with graceful shutdown flush.
  - `GET /api/links/{code}/stats` endpoint (total clicks and 30-day daily breakdown).
  - ADR 0004 covering async batching, accepted data loss trade-offs, and privacy (no raw IP).

### Known Gaps
- Rate limiting middleware (Phase C).
- Benchmark harness updates for 80/20 hot set distribution (Phase D).
- Architecture diagram in main README (Phase D).
