# 4. Asynchronous Click Analytics and Privacy Strategy

Date: 2026-11-01  
Status: Accepted  

## Context

Tracking link usage (click counts, referrers, and user agents) provides critical analytics for shortened links. However, executing database inserts directly within the redirect request pipeline introduces unacceptable latency, limits peak redirect throughput, and couples redirect availability directly to write database health.

Key engineering and compliance considerations:
1. **Redirect Latency**: The redirect endpoint (`GET /{code}`) must remain on an ultra-fast path (<5ms p95), free of synchronous write I/O.
2. **Channel Capacity & Backpressure**: Surges in click events must not cause unbounded memory consumption or out-of-memory errors.
3. **Data Loss Trade-off**: Balancing in-memory buffer resilience versus crash recovery (`kill -9` vs. graceful shutdown).
4. **Privacy**: Compliance with user privacy standards (GDPR/ePrivacy) and avoiding the storage of Personally Identifiable Information (PII).

---

## Decisions

### 1. Decoupled In-Memory Bounded Channel
- **Decision**: In the redirect handler, click events are written synchronously to an in-memory `Channel<ClickEvent>` using non-blocking `TryWrite`.
- **Buffer Size & Policy**: The channel has a fixed capacity of **10,000 events** with `BoundedChannelFullMode.DropOldest`.
- **Zero Latency Overhead**: The redirect path never awaits database operations, connection pools, or transactions. If the buffer is full during an extreme surge, the oldest event is evicted, and a `dropped_clicks` counter (`System.Diagnostics.Metrics`) is incremented.

### 2. Batched Background Persistence Worker
- **Decision**: A hosted `BackgroundService` (`ClickAnalyticsBatchProcessor`) consumes the channel and bulk-inserts events into PostgreSQL.
- **Batching Trigger**: Batches are flushed to the database when either of two thresholds is met:
  1. **Batch Size Threshold**: 500 events accumulated.
  2. **Time Threshold**: 2 seconds elapsed since the last flush.
- **Graceful Shutdown**: On `StopAsync`, the worker drains all remaining queued events from the channel and flushes them to the database before the application process exits.

### 3. Accepted Data-Loss Trade-Off on Hard Crash
- **Decision**: Accept the loss of at most 2 seconds / 500 in-memory events strictly in the event of an abrupt, ungraceful crash (e.g., immediate power outage or SIGKILL / `kill -9`).
- **Rationale**: For an analytics system, sub-millisecond redirect latency and 99.999% availability far outweigh 100% durability for click analytics during a catastrophic node failure. Graceful shutdowns (SIGTERM / container stop) flush all remaining in-memory events cleanly without loss.

### 4. Privacy: Zero Raw IP Storage
- **Decision**: Raw IP addresses are **never stored** in the `click_events` table.
- **Rationale**: IP addresses are considered personal data under GDPR and modern privacy regulations. Storing raw IPs creates compliance liabilities, requires user consent, and increases security exposure. Storing truncated `Referrer` (max 200 chars) and `UserAgent` (max 200 chars) fulfills analytics needs while preserving privacy by design.

---

## Consequences

- **Positive**:
  - Redirect throughput remains completely unconstrained by relational write limits.
  - Constant, predictable memory footprint capped by the 10,000-event bounded channel.
  - Zero PII compliance footprint; privacy-friendly analytics.
  - Resilient graceful shutdown draining with zero data loss under standard deployments and rolling restarts.
- **Negative / Considerations**:
  - Analytics numbers display up to a 2-second eventual consistency lag behind live redirects.
  - Hard crashes (SIGKILL) may lose up to 500 buffered events.
