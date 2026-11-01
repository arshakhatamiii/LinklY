# 3. Redis Cache-Aside and Stampede Protection Strategy

Date: 2026-11-01  
Status: Accepted  

## Context

Redirect lookups (`GET /{code}`) are the dominant workload of a URL shortener, accounting for >95% of incoming traffic. Hitting PostgreSQL on every redirect introduces a throughput ceiling and elevates read latency. To scale redirect throughput to thousands of requests per second, a caching layer is necessary.

However, caching brings challenges:
1. **Cache Pattern**: Cache-aside vs. Write-through.
2. **Cache Stampede / Thundering Herd**: Massive concurrent misses on hot keys hitting the database simultaneously.
3. **Cache Penetration**: Repeated requests for non-existent codes exhausting backend database resources.
4. **Resilience & Failure Modes**: Redis downtime or latency spikes must not degrade core application availability.
5. **Expiration Integrity**: Cached entries must never resurrect expired links.

---

## Decisions

### 1. Cache-Aside over Write-Through
- **Decision**: Adopt the **Cache-Aside** pattern. The application checks Redis on read; if missed, it reads from PostgreSQL and populates Redis.
- **Rationale**: URL creation (`POST /api/links`) is infrequent relative to redirects. Write-through incurs write latency and caches links that may never be visited immediately. Cache-aside ensures that only frequently accessed links consume Redis memory.

### 2. TTL Strategy & Read-Time Expiry Verification
- **Key Schema**: `link:{code}` storing JSON `{ url, expiresAtUtc, isNotFound }`.
- **TTL Calculation**: `min(24 hours, remaining time until expiresAtUtc)`.
- **Integrity Guarantee**: When reading from cache, the application checks `ExpiresAtUtc` against `TimeProvider.GetUtcNow()`. If expired, it immediately returns `410 Gone`. Caching never resurrects an expired resource even if Redis TTL clock drifts.

### 3. Negative Caching to Mitigate Penetration
- **Decision**: Non-existent codes queried through `GET /{code}` are cached with a sentinel `{ isNotFound: true }` and a short **60-second TTL**.
- **Invalidation**: Whenever a new link or custom alias is created (`POST /api/links`), `ILinkCache.RemoveAsync(code)` is executed to immediately bust any negative entry and prevent false 404s.

### 4. In-Process Single-Flight for Stampede Protection
- **Decision**: An in-process `ISingleFlight` coordinator (`SingleFlight`) deduplicates concurrent queries for the same key.
- **Mechanism**: When N concurrent requests miss the cache for key `X`, exactly one request executes the database query and populates the cache. The remaining N - 1 requests await that flight's `Task` and receive the result directly, completely eliminating thundering herd load on PostgreSQL.

### 5. Resilience, Fail-Open Circuit, and Metrics
- **Timeout**: Redis operations configure `AsyncTimeout = 50ms`. Slow Redis calls do not tie up request threads.
- **Fail-Open Fallback**: If Redis fails or times out, the error is logged as a warning, and the request transparently falls back to PostgreSQL.
- **Backoff Circuit**: Upon an error, Redis calls are short-circuited for 5 seconds (`BackoffDuration`), preventing cascade timeouts on a dead Redis instance.
- **Observability**: `cache_hits`, `cache_misses`, and `cache_errors` are recorded via `System.Diagnostics.Metrics` and exposed via `/internal/stats`.
- **Toggle**: `Caching:Enabled` allows running benchmarks and tests with caching disabled without requiring a new build.

---

## Consequences

- **Positive**:
  - Dramatic latency reduction on hot redirect keys.
  - Resilience against Redis crashes (zero downtime fallback to PostgreSQL).
  - Protection against thundering herd spikes and cache penetration.
  - Deterministic testing with `Testcontainers.Redis` and `FakeTimeProvider`.
- **Negative / Considerations**:
  - In a multi-replica deployment, in-process single-flight deduplicates per pod; across pods, at most 1 query per pod hits the database during stampede.
