# 5. Rate Limiting Strategy and Reverse Proxy Topology

Date: 2026-11-01  
Status: Accepted  

## Context

To prevent abuse, denial-of-service (DoS), and resource exhaustion, endpoints must be rate-limited per client IP. Specifically:
1. `POST /api/links` (write operations creating short links/aliases) requires a strict sliding window limit of 20 requests per minute per IP.
2. `GET /{code}` (redirect hot path) must not be throttled by default, but rate limiting must be optionally configurable via `appsettings.json`.
3. Requests exceeding limits must receive RFC 7807 `ProblemDetails` with HTTP status `429 Too Many Requests` and a standard `Retry-After` header.
4. The service runs behind reverse proxies (Nginx, Envoy, Cloudflare, Traefik), requiring accurate client IP resolution via `ForwardedHeaders`.

---

## Decisions

### 1. In-Memory vs. Distributed (Redis) Rate Limiting
- **Decision**: Adopt ASP.NET Core's built-in in-memory `RateLimiter` middleware (`Microsoft.AspNetCore.RateLimiting`) using `RateLimitPartition.GetSlidingWindowLimiter`.
- **Comparison & Trade-offs**:
  - *Distributed Limiter (Redis token bucket/fixed window)*: Provides shared global counters across multiple application nodes, but introduces network round-trips (serialization and latency) on the request pipeline and makes rate limiting dependent on Redis availability.
  - *In-Memory Limiter*: Runs in zero-latency process memory with no external dependencies or network overhead.
- **Single-Instance Justification**: For single-instance deployments (standard Docker Compose setup), an in-memory sliding window limiter is 100% accurate with zero latency overhead. In multi-pod deployments, if desired, per-pod in-memory limiting effectively provides aggregate capacity ($N \times \text{limit}$) or can be upgraded to distributed Redis scripts if strict cross-cluster enforcement is required.

### 2. Sliding Window Policy on Write Endpoint
- **Configuration**:
  - `PermitLimit`: 20 requests
  - `Window`: 60 seconds (1 minute)
  - `SegmentsPerWindow`: 6 (10-second segments for smooth sliding window evaluation)
  - `QueueLimit`: 0 (reject immediately without thread-blocking queues)
- **429 Response Format**:
  - Emits `429 Too Many Requests` as `application/problem+json` containing standard RFC 7807 metadata (`Title: "Too Many Requests"`, `Status: 429`, `Detail: "Rate limit exceeded. Please try again later."`).
  - Calculates remaining window seconds and attaches standard `Retry-After: <seconds>` HTTP header.

### 3. Forwarded Headers and IP Spoofing Risks
- **Configuration**:
  - `ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto`.
  - In local and containerized topologies, `KnownIPNetworks.Clear()` and `KnownProxies.Clear()` allow `X-Forwarded-For` from reverse proxy containers to populate `Connection.RemoteIpAddress`.
- **Security / Spoofing Risk Notice**:
  > [!WARNING]
  > When `KnownIPNetworks` and `KnownProxies` are cleared without an edge reverse proxy that overwrites `X-Forwarded-For`, untrusted external clients can inject arbitrary `X-Forwarded-For: <fake-ip>` headers to rotate client IPs and circumvent rate limiting. In production, ingress infrastructure (e.g., Cloudflare, Nginx, ALB) MUST be configured to overwrite or validate `X-Forwarded-For` with the true client socket address, or explicit trusted proxy CIDRs must be configured in `ForwardedHeadersOptions`.

---

## Consequences

- **Positive**:
  - Zero external latency on rate limiting checks.
  - Granular per-IP sliding window protection against URL creation abuse.
  - Standard RFC 7807 problem details and `Retry-After` headers for compliant client backoff.
  - Redirect hot path remains unhindered by default while retaining toggleable rate limiting.
- **Negative / Considerations**:
  - In multi-instance deployments, each instance maintains its own local memory bucket.
  - Edge reverse proxy must strip untrusted `X-Forwarded-For` headers.
