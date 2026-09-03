# ADR-0008: In-tree FSRS-6 Core scheduling foundation

**Status:** Accepted
**Decision date:** 2026-08-28

## Context

KnownFirst requires a modern, evidence-based spaced repetition scheduling algorithm to optimize vocabulary retention while minimizing unnecessary review workload. The initial application implementation used `SimpleSpacedRepetitionScheduler` with fixed ease factors, heuristic interval growth, and a 365-day mastery ceiling.

The Free Spaced Repetition Scheduler (FSRS) represents the current state of the art in open spaced-repetition modeling, based on the Three-Component Model of Memory (retrievability, stability, difficulty). FSRS-6 is the established, stable production iteration of this model. FSRS-7 remains in development and is explicitly not a target.

Integrating an algorithm of this complexity into an offline-first, NativeAOT-compiled .NET 10 application on Windows and Android presents key architectural constraints:
1. Trimming and NativeAOT safety (no reflection, dynamic code, or runtime Python/bridge dependencies).
2. Strict determinism (no unseeded randomness or fuzz in core mathematical calculations).
3. Zero external runtime dependencies in `KnownFirst.Core`.
4. Pure separation between mathematical scheduling/replay and database/UI concerns.
5. Controlled migration risk: delivering the core mathematical engine as an independent, fully tested foundation before altering production persistence or application composition.

## Decision

KnownFirst adopts an in-tree, deterministic, platform-neutral FSRS-6 scheduling and replay foundation in `KnownFirst.Core.Learning.Fsrs6`.

At the time of this decision, the package (`KF-FSRS6-CORE-001`) delivered this pure mathematical engine and test oracle without modifying production scheduler composition, `LearningService`, dependency injection, or persistence. The then-current `SimpleSpacedRepetitionScheduler` remained the active Schema-12 scheduler pending a separate governed integration package. That historical boundary was later superseded by merged `KF-FSRS-003`; current production scheduling is owned by `IFsrs6SchedulingService` / `Fsrs6SchedulingService`.

### 1. Pinned Production Configuration

The engine uses a fixed, immutable parameter set (`Fsrs6Parameters`):
- **Weights:** Exactly 21 parameters matching upstream `DEFAULT_PARAMETERS` ($w_0..w_{19}$) and `FSRS_DEFAULT_DECAY = 0.1542` ($w_{20}$):
  `[0.212, 1.2931, 2.3065, 8.2956, 6.4133, 0.8334, 3.0194, 0.001, 1.8722, 0.1666, 0.796, 1.4835, 0.0614, 0.2629, 1.6483, 0.6014, 1.8729, 0.5425, 0.0912, 0.0658, 0.1542]`
- **Desired Retention:** `0.90` (strictly validated $0.0 < r < 1.0$).
- **Learning Steps:** Single 10-minute step (`[10]`).
- **Relearning Steps:** Single 10-minute step (`[10]`).
- **Maximum Interval:** 36,500 days (100 years).
- **Fuzz:** Explicitly disabled (`EnableFuzz = false`; attempting to enable fuzz fails closed).
- **Arithmetic:** Standard IEEE-754 binary64 / C# `double`.

### 2. State Model and Invariants

The core state model (`Fsrs6CardState` and `Fsrs6Card`) enforces strict invariants:
- **States:** `New` (0), `Learning` (1), `Review` (2), `Relearning` (3).
- **New Cards:** Stability, Difficulty, LastReviewedAtUtc, and StepIndex must all be `null`.
- **Active Cards (`Learning`, `Review`, `Relearning`):**
  - Stability $\ge 0.001$ (`MinimumStability`), finite.
  - Difficulty $\in [1.0, 10.0]$ (`MinimumDifficulty` .. `MaximumDifficulty`), finite.
  - `LastReviewedAtUtc` must be a valid UTC timestamp (`Offset == TimeSpan.Zero`).
  - `StepIndex` must be `0` for `Learning` and `Relearning`, and `null` for `Review`.

### 3. Rating and Transition Semantics

Ratings reuse `KnownFirst.Core.Learning.ReviewRating` mapped to FSRS grades 1..4:
- `Again` (Grade 1 — Failed Recall): Lapses to `Learning` (from `New`/`Learning`) or `Relearning` (from `Review`/`Relearning`) with a 10-minute due interval; uses the delayed forget equation when elapsed whole days $> 0$.
- `Hard` (Grade 2 — Successful Effortful Recall): Schedules a 15-minute due interval in `Learning`/`Relearning`; in `Review`, updates stability using the recall equation with the $w_{15}$ Hard penalty and graduates or updates the review interval; same-day ratings enforce a stability floor multiplier $\ge 1.0$.
- `Good` (Grade 3 — Normal Successful Recall): Graduates `New`, `Learning`, and `Relearning` cards to `Review` with calculated interval; updates `Review` cards via standard recall equation.
- `Easy` (Grade 4 — Immediate Easy Recall): Graduates to `Review` or updates `Review` cards using the recall equation with the $w_{16}$ Easy bonus.

Elapsed time uses deterministic elapsed whole days ($\lfloor\text{totalDays}\rfloor$). Fractional intervals under 24 hours use the same-day stability equation; exact 24 hours ($1.0$ days) transitions deterministically to the delayed recall/forget equations. Calculated intervals use round-to-even (`MidpointRounding.ToEven`) clamped to $[1, 36500]$.

### 4. Deterministic History Replay

`Fsrs6Replayer` derives the current scheduling state of a card by replaying an ordered sequence of factual review events (`Fsrs6ReviewEvent`):
- Replay is a pure function delegating each transition to `Fsrs6Scheduler.Schedule(...)`.
- Preserves caller-supplied total order for events sharing identical timestamps.
- Fails closed (`ArgumentOutOfRangeException`) upon chronological reversal.
- Does not mutate input instances or allocate heap objects during streaming.
- Persistence identities (e.g. database `StableId` ordering) remain isolated from this Core contract.

### 5. Verification Strategy and Cross-Language Test Oracle

FSRS-6 formula fidelity is verified against the pinned upstream implementation:
- **Project:** `open-spaced-repetition/py-fsrs`
- **Version:** `v6.3.2`
- **Commit:** `9446cb06605c597a063aeee49f7d188d42e34dc2`
- **Reference:** `fsrs/scheduler.py`

A static fixture of 38 precomputed oracle vector histories (`Fsrs6OracleVectors.cs`) is committed in-tree. Normal test execution requires no Python runtime or network access. Floating-point stability and difficulty comparisons use an absolute tolerance of $\le 10^{-12}$ or relative tolerance of $\le 10^{-10}$. Discrete states, step indices, and due timestamps must match exactly.

## Consequences

### Positive
- Platform-neutral, trim-safe, and NativeAOT-compatible implementation in `KnownFirst.Core`.
- Zero third-party runtime dependencies, Python dependencies, or network requirements.
- Full mathematical determinism without hidden randomness or fuzz.
- High test confidence provided by cross-language test vectors generated directly from `py-fsrs v6.3.2`.
- Streaming replay allocates zero objects per review event.

### Limitations and Costs
- Mathematical scheduling only: FSRS does not define application-level card retirement or permanent-known vocabulary cleanup (governed separately by KnownFirst architecture).
- Does not yet alter user-visible learning behavior until the persistence and service cutover package is executed.
- On-device parameter optimization is not included; production uses the pinned 21 default parameters.

## Alternatives Considered

- **External C# or NuGet FSRS Packages:** Rejected due to uncertain maintenance, trimming/AOT risks, and unwanted dependencies.
- **Python Runtime Bridge / Sidecar:** Rejected due to startup overhead, packaging complexity on Android, and violation of the local-first NativeAOT architecture.
- **FSRS-7 Target:** Rejected because FSRS-7 is not yet stabilized or standardized in the reference implementation ecosystem.
- **Immediate Monolithic Cutover:** Rejected in favor of a layered, multi-package architecture: delivering the verified Core engine first, followed by clean persistence integration.
# Current runtime note

The FSRS-6 core foundation described here is now consumed by the production runtime through the merged `KF-FSRS-003` cutover. This note does not alter the ADR decision or authorize later cleanup; `KF-CLEANUP-001` remains deferred.
