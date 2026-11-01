# 2. Custom Alias and Link Expiration Strategy

Date: 2026-11-01  
Status: Accepted  

## Context

Following the core URL shortener foundation, users require two key enhancements:
1. **Custom Aliases**: Allowing users to specify readable, vanity URLs (e.g., `https://sho.rt/launch-2026`).
2. **Link Expiration**: Providing time-bounded links that automatically become unavailable after a specified timestamp.

Both capabilities introduce potential concurrency vulnerabilities (race conditions on alias creation), routing ambiguities (conflicts with API endpoints), and testability challenges (non-deterministic time checks).

---

## Decisions

### 1. Alias Constraints and Reserved Words Prevention
- **Length & Character Set**: Custom aliases must be between **3 and 30 characters** and conform to `^[a-zA-Z0-9_-]{3,30}$`.
- **Reserved Words**: High-priority application routes (`api`, `health`, `swagger`, `healthz`, `metrics`) are reserved via a configurable list in `appsettings.json`. Requests attempting to use reserved words are rejected with `400 Bad Request` ProblemDetails before querying persistence.

### 2. Uniqueness Enforcement via Database Unique Index (No Check-Then-Insert)
- **Anti-Pattern Rejected**: "Check-then-insert" (`if (!db.Links.Any(x => x.Code == alias)) db.Add(...)`) suffers from a classic **TOCTOU (Time-of-Check to Time-of-Use)** race condition. Under concurrent requests, two workers could both see the alias as available and attempt to insert.
- **Decision**: Uniqueness is enforced strictly at the storage engine level via PostgreSQL's `UNIQUE INDEX` on `short_links(Code)`. The API handles `DbUpdateException` where `PostgresException.SqlState == PostgresErrorCodes.UniqueViolation` (`"23505"`) and maps it to an RFC 7807 compliant `409 Conflict` ProblemDetails.

### 3. Expiration Semantics: 410 Gone vs. 404 Not Found
- **Decision**: Expired links return **`410 Gone`**, distinct from **`404 Not Found`**.
  - `404 Not Found` indicates the resource never existed or is not currently located.
  - `410 Gone` explicitly signals to clients, search engines, and crawlers that the resource previously existed but is intentionally and permanently expired.

### 4. Deterministic Time via Injected `TimeProvider`
- **Decision**: All system time references use the framework-native `TimeProvider` abstraction (`TimeProvider.GetUtcNow()`) rather than ambient static calls (`DateTime.UtcNow`).
- **Consequence**: In integration and unit tests, a `FakeTimeProvider` allows testing time-dependent expiration logic deterministically (fast-forwarding time instantly) without brittle sleeps or wall-clock dependencies.

---

## Consequences

- **Positive**:
  - Zero race conditions on alias reservation; concurrency safety guaranteed by PostgreSQL.
  - Strict separation between application routes and user-defined vanity aliases.
  - Highly deterministic, millisecond-fast integration test suite for temporal behavior.
  - Accurate HTTP semantics with `409 Conflict` and `410 Gone`.
- **Negative / Considerations**:
  - Expired records remain in the database table until a background pruning worker or soft-delete cleaner is introduced in future iterations.
