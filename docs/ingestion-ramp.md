# Ingestion ramp — raising match throughput in stages

How to raise the match-fetch workers' throughput (#1457) one stage at a time, and how the
resource backpressure that guards each stage behaves (#1459). This is an operator runbook:
it names settings and admin panels, never hosts. Where the environment lives and how to
reach it is in the operator-only notes, not here.

Why it is staged: ingestion volume has taken the stack down three times, each time at full
speed and without warning — an OOM kill during the champion pattern aggregation (#600), `/dev/shm`
exhaustion during parallel aggregate queries (#589), a full disk that crashed Mongo and the API
(#680). Once the permanent workers removed the wall-clock ceiling on match fetching, the
database became the binding constraint, so throughput goes up one measured step at a time,
behind a brake that engages before those walls.

## The stages

Each stage is a target rate: the most Riot calls a match-fetch worker makes per minute on its
regional host (`americas`, `europe`, `asia`). The rate limiter holds the workers to it, beside
the shares it reserves for `AccountRefresh` and `ManualSeed`.

| Stage | Calls / min per regional host | Note |
| ----- | ----------------------------- | ---- |
| 1 | 30 | production's average before the workers (#1457) — the default |
| 2 | 36 | |
| 3 | 40 | |
| 4 | 44 | the personal-key ceiling: 50 sustained, 47.5 after the limiter's 5 % headroom, less the default reservations |

The list is `MatchFetchWorkers:RampStages`; it must climb strictly, and the boot fails
otherwise. A production key advertises a far larger budget: its ramp is a new list, written
when the key lands, not more stages appended to this one.

## Raising one stage

1. **Check the current stage is settled** — every exit criterion below holds over at least
   one full day at the current stage. The 30-day window of the Riot API tab is what makes a
   step readable against the days before it.
2. **Raise it** — set `INGESTOR_FETCH_RAMP_STAGE` in the environment's env file to the next
   stage (it maps to `MatchFetchWorkers__RampStage`, counted from 1) and redeploy the fetch
   container. Preprod first; prod only once preprod has held the stage.
3. **Confirm it took** — the admin *Configuration* page shows `MatchFetchWorkers:RampStage`
   with its new value, and the ingestor's startup log line names the stage and the target.
4. **Hold and measure** — at least a day, then the exit criteria again.

Never skip a stage, and never raise on prod a stage preprod has not held.

### Exit criteria of a stage

Read in the admin portal:

- **Riot API tab** — `MatchIngestion` near the target on each regional host; the 429 rate flat
  against the previous stage; `AccountRefresh` and `ManualSeed` still completing their regional
  calls.
- **Processes** — `Discovery`, `LadderSync`, `MainActivity` and `AccountRefresh` keep their
  cadence; the fetch-worker batches follow one another; no `IngestionBackpressureActivated`
  in the logs, or each one released within the hour.
- **Database / storage** — the daily storage snapshots: growth per day at this stage, and the
  forecast staying clear of the disk. Retention (`MatchDataRetention`, timeline pruning, #772 /
  #773) has to keep the stock bounded at the new rate: the size measured right after a
  retention pass must stop climbing from one day to the next once the retained patches are full.
  If it keeps climbing, the stage is too high for the retention window — step back.
- **Aggregate lane** — its passes still finish inside its interval; a lane that falls behind
  means Postgres, not Riot, is the ceiling.

### Stepping back

Set `INGESTOR_FETCH_RAMP_STAGE` to the previous stage and redeploy the fetch container. The
workers pick it up at boot; nothing is lost — a stage only sets how fast they claim.

## Resource backpressure

Before every claim, each worker reads the host's memory and free disk. Below a threshold, the
workers stop claiming until the resource recovers:

| Setting | Env variable | Default | Reads |
| ------- | ------------ | ------- | ----- |
| `IngestionBackpressure:MinAvailableMemoryPercent` | `INGESTOR_BACKPRESSURE_MIN_AVAILABLE_MEMORY_PERCENT` | 10 | `MemAvailable` / `MemTotal` from `/proc/meminfo` — the host's figures, seen from inside the container |
| `IngestionBackpressure:MinFreeDiskPercent` | `INGESTOR_BACKPRESSURE_MIN_FREE_DISK_PERCENT` | 15 | free share of the disk holding `IngestionBackpressure:DiskPath` (`/`, which sits on the host's Docker data root) |
| `IngestionBackpressure:ReleaseMarginPercent` | — | 3 | points above the threshold a resource must recover before claims resume |
| `IngestionBackpressure:ProbeIntervalSeconds` | — | 30 | how often held-back workers read again |
| `IngestionBackpressure:DrillMinutes` | `INGESTOR_BACKPRESSURE_DRILL_MINUTES` | 0 (off) | holds the brake for that long from the first claim, for verification |

Memory and disk only — the two resources behind the past outages (`/dev/shm` counts as
memory). CPU is out of scope until a real CPU problem is observed.

What it guarantees:

- **Throughput degrades, correctness does not.** The brake sits in front of the claim and
  nowhere else: a batch already claimed is fetched and written to the end, so no fetched match
  is dropped and no claim is left stranded. Only the next claim waits.
- **It stops rather than slows.** A fraction of the rate would keep filling the disk or the
  memory that tripped it.
- **Every transition is an ops event**, logged once however many workers see it:
  `IngestionBackpressureActivated` (the resource, its reading, the threshold) and
  `IngestionBackpressureReleased` (how long it held). Filter on them in the admin *Logs* page.
- A host that cannot report a resource logs one warning at the first claim and applies no brake
  on that resource.

### Verifying the brake on preprod

Proved on the live workers, never by filling a disk or starving the host. Two ways, both
reverted by putting the variable back and redeploying the fetch container:

**A drill — activation and release.** Set `INGESTOR_BACKPRESSURE_DRILL_MINUTES` (say `20`)
and redeploy. From the first claim, the brake holds for that long as if a resource were below
its threshold, then reads the real resources again. Expect:

1. an `IngestionBackpressureActivated` event naming the drill, at the first claim;
2. the batch that was in flight completing normally, and no new `MatchIngestion` batch for
   the drill's duration;
3. no account left `Processing` (the admin account explorer, or the candidates-processing
   panel);
4. an `IngestionBackpressureReleased` event once the drill ends, and the batches resuming.

**A raised threshold — the real readings.** Set `INGESTOR_BACKPRESSURE_MIN_FREE_DISK_PERCENT`
above the disk's current free share (or `INGESTOR_BACKPRESSURE_MIN_AVAILABLE_MEMORY_PERCENT`
above the available memory) and redeploy: the activation event carries the real reading and
the threshold, and the workers stay held until the variable is put back. The redeploy that
restores it starts a fresh process, so this path shows the activation only; the drill and the
unit tests (`IngestionBackpressureTests`) cover the release.
