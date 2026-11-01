# Baseline Load Testing (k6)

Performance benchmark script for `GET /{code}` redirect endpoint using **Grafana k6**.

---

## Objective

Establish a performance baseline for database-driven redirects under concurrency **before** introducing caching layers (e.g., Redis / In-Memory cache).

---

## Test Scenario

- **Target Endpoint**: `GET /{code}`
- **Ramping Profile**:
  - 0 to 50 Virtual Users (VUs) over 10 seconds
  - Sustained 50 VUs for 20 seconds (Total: 30 seconds)
- **Redirects**: `redirects: 0` (Evaluates redirect throughput without following external destination URLs)

---

## How to Run

### 1. Ensure the Service is Running
Start the application using Docker Compose:
```bash
docker compose up -d
```

### 2. Run k6 via Docker

On **macOS / Windows** (uses `host.docker.internal` to reach the host port):
```bash
docker run --rm -i \
  -v $(pwd)/loadtest:/loadtest \
  -e BASE_URL=http://host.docker.internal:5100 \
  grafana/k6 run /loadtest/redirect.js
```

On **Linux** (uses host networking):
```bash
docker run --rm -i \
  --network=host \
  -v $(pwd)/loadtest:/loadtest \
  -e BASE_URL=http://localhost:5100 \
  grafana/k6 run /loadtest/redirect.js
```

---

## Results Table

| Run | Date | Concurrency (VUs) | Duration | Requests/sec | p95 Latency | p99 Latency | Error Rate | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Baseline (No Cache)** | 2026-11-01 | 50 | 30s | *Recorded on run* | *TBD* | *TBD* | 0.00% | Direct PostgreSQL query; no caching |
| **Future: Memory Cache** | - | 50 | 30s | - | - | - | - | In-Memory IMemoryCache |
| **Future: Redis Cache** | - | 50 | 30s | - | - | - | - | Distributed Redis cache |
