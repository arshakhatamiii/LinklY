# Load Testing & Performance Benchmark (k6)

Performance stress testing harness for `GET /{code}` redirect throughput using **Grafana k6**.

---

## Benchmark Scenarios

The benchmark seeds **$N = 5,000$** short links via `POST /api/links` during setup, then executes a `ramping-arrival-rate` executor ramping from 1,000 to 15,000 target iterations/sec across 5 stages (20s each, total 100s).

Two distribution modes are selectable via the `SCENARIO` environment variable:
1. **`uniform`** (default): Every request picks uniformly at random from the 5,000 seeded links.
2. **`hotset`** (Pareto 80/20 distribution): **80% of traffic** hits a hot working set of **20% of codes** ($1,000$ links), while the remaining 20% of traffic is distributed over the other 80% ($4,000$ links).

---

## How to Run

### 1. Configure Caching Mode

The application supports toggling Redis caching via the `Caching__Enabled` environment variable on the same build:

#### Running with Cache Enabled (Default):
```bash
Caching__Enabled=true docker compose up -d --build
```

#### Running with Cache Disabled:
```bash
Caching__Enabled=false docker compose up -d --build
```

---

### 2. Execute k6 via Docker

#### Uniform Random Scenario:
```bash
# macOS / Windows (host.docker.internal)
docker run --rm -i \
  -v $(pwd)/loadtest:/loadtest \
  -e BASE_URL=http://host.docker.internal:5100 \
  -e SCENARIO=uniform \
  grafana/k6 run /loadtest/redirect.js

# Linux (host networking)
docker run --rm -i \
  --network=host \
  -v $(pwd)/loadtest:/loadtest \
  -e BASE_URL=http://localhost:5100 \
  -e SCENARIO=uniform \
  grafana/k6 run /loadtest/redirect.js
```

#### Hot-Set (80/20) Scenario:
```bash
docker run --rm -i \
  -v $(pwd)/loadtest:/loadtest \
  -e BASE_URL=http://host.docker.internal:5100 \
  -e SCENARIO=hotset \
  grafana/k6 run /loadtest/redirect.js
```

---

### 3. Reading `dropped_iterations`

When running k6 with `ramping-arrival-rate`, the test configures a ceiling of `maxVUs: 1000`.
- **Target Rate**: The arrival-rate executor attempts to initiate $R$ requests per second.
- **Sustained Throughput**: If the system responds within the allocation window, all iterations execute successfully (`http_reqs`).
- **`dropped_iterations`**: If requests take longer to complete and all 1,000 VUs are busy waiting for responses, k6 cannot start new iterations at the target arrival rate and increments `dropped_iterations`.
- **Interpreting Results**:
  - `dropped_iterations == 0`: The system fully absorbed the target rate without VU starvation.
  - `dropped_iterations > 0`: The target rate exceeded the system's maximum sustainable capacity. The last stage where `dropped_iterations` remained near zero defines the **Max Sustainable req/s**.

---

## Results Table

> [!NOTE]
> All rows contain `TODO` placeholders to be populated from local benchmark runs.

| Cache (Off/On) | Scenario | Max Sustainable req/s | p95 Latency | p99 Latency | Error Rate | Environment |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Off** (`Caching__Enabled=false`) | `uniform` | *TODO* | *TODO* | *TODO* | *TODO* | *TODO (e.g. Docker Compose, Apple M-series / x86_64)* |
| **Off** (`Caching__Enabled=false`) | `hotset` (80/20) | *TODO* | *TODO* | *TODO* | *TODO* | *TODO (e.g. Docker Compose, Apple M-series / x86_64)* |
| **On** (`Caching__Enabled=true`) | `uniform` | *TODO* | *TODO* | *TODO* | *TODO* | *TODO (e.g. Docker Compose, Apple M-series / x86_64)* |
| **On** (`Caching__Enabled=true`) | `hotset` (80/20) | *TODO* | *TODO* | *TODO* | *TODO* | *TODO (e.g. Docker Compose, Apple M-series / x86_64)* |
