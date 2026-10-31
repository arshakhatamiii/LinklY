# 1. Short Code Generation Strategy: PostgreSQL Sequence + Base62 vs. Random Token Generation

Date: 2026-10-31  
Status: Accepted  

## Context

A URL shortener requires generating a compact, unique string (the "short code") representing a target URL (e.g. `https://sho.rt/4c92`).
The mechanism used to generate these codes directly impacts system throughput, database scalability, storage requirements, collision handling complexity, and enumeration security.

Two primary approaches were evaluated for the core URL shortening engine:
1. **Database Sequence + Base62 Encoding (Counter-based)**
2. **Cryptographically Random / Pseudo-Random String Generation (e.g., UUID, NanoID)**

---

## Decision

We chose **PostgreSQL Sequence + Base62 Encoding** isolated behind the `IShortCodeGenerator` interface.

---

## Detailed Trade-off Analysis

### 1. Collision Probability & Concurrency
- **Base62 (Counter / Sequence)**:
  - **Zero collisions by design.** Each ID from PostgreSQL's `sequence` is guaranteed to be strictly unique across concurrent threads and multiple API instances without distributed locking.
  - No retry loops or optimistic concurrency conflicts upon insert.
- **Random String Generation**:
  - Bound by the **Birthday Paradox**. As the number of stored URLs grows, collision probability increases non-linearly.
  - Requires insert-retry loops or pre-checking existence before saving, leading to elevated database roundtrips, transaction rollbacks, or deadlocks under high write concurrency.

### 2. Database Index & Write Performance
- **Base62 (Counter / Sequence)**:
  - Sequential 64-bit integer keys (`bigint`) result in monotonically increasing values.
  - B-Tree indexes on the primary key experience minimal page fragmentation and high cache locality, allowing PostgreSQL to write data efficiently to append-only leaf pages.
- **Random String Generation**:
  - Randomly generated strings insert into random locations in the B-Tree index, causing page splits, increased I/O, and cache thrashing as data volume scales beyond RAM.

### 3. Code Length & Information Density
- **Base62** uses 62 alphanumeric characters (`0-9`, `a-z`, `A-Z`):
  - A 6-character Base62 string can encode $62^6 \approx 56.8 \text{ billion}$ unique URLs.
  - A 7-character string can encode $62^7 \approx 3.52 \text{ trillion}$ unique URLs.
  - Starting our sequence at `1,000,000` guarantees codes start at 4 characters (`4c92`), avoiding awkward 1- or 2-letter tokens.
- **Random Strings**:
  - To keep collision rates negligibly low without constant retry loops, random strings require greater length (e.g., 8–12 characters), producing longer URLs.

### 4. Predictability & Enumeration (Security Consideration)
- **Base62 (Counter / Sequence)**:
  - *Trade-off*: Monotonic sequence values are predictable (`4c92`, `4c93`, etc.). An attacker could scrape or enumerate all shortened links by sequentially iterating over codes.
  - *Mitigation*: The code generator is strictly isolated behind `IShortCodeGenerator`. In future extensions requiring unguessable URLs, a reversible permutation (such as a 64-bit Feistel cipher or multiplicative inverse) can be plugged into the generation pipeline without touching table schemas or API layers.
- **Random Strings**:
  - Non-enumerable by default, providing obscurity against casual crawling, but with the performance and collision penalties noted above.

---

## Consequences

- **Positive**:
  - 100% deterministic, collision-free code generation with single-roundtrip database persistence.
  - High write throughput and optimal B-Tree index storage in PostgreSQL.
  - Minimal implementation footprint with clean testability.
- **Negative / Known Limitation**:
  - Generated URLs are sequential and enumerable.
  - *Upgrade Path*: If enumeration prevention is required in the future, implement a Feistel permutation or SKIP32 algorithm inside `IShortCodeGenerator` before Base62 encoding.
