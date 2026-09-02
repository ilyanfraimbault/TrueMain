# Riot API key switch

Runbook for replacing the Riot API key an environment runs on — the personal
key by an approved production key, or any key by another one. It is written for
the production switch, but every step is meant to be rehearsed on preprod first
(see [Rehearsal on preprod](#rehearsal-on-preprod)).

Read it end to end before touching anything: a key switch is the single most
destructive routine operation on this project, and the damage is silent.

## Why a key switch is destructive

**Riot encrypts every identifier it hands out per API key.** The PUUID of a
given player under key A and the PUUID of the same player under key B are
different strings. Nothing about them says so — they are the same shape, the
same length, and neither Riot nor Postgres rejects the old one. The failure
mode is a `404` per account and a database that quietly stops describing
anybody.

Every table below stores a PUUID, and each of them becomes meaningless the
moment the key changes:

| Table                 | Column               | What breaks                                                       |
| --------------------- | -------------------- | ----------------------------------------------------------------- |
| `riot_accounts`       | `"Puuid"` (unique)   | the account no longer resolves against account-v1 or match-v5      |
| `match_participants`  | `"Puuid"`            | historical participants no longer join to the account that played them |
| `main_champion_stats` | `"Puuid"`            | mains are keyed by `("PlatformId", "Puuid", "ChampionId")` — the whole roster orphans |
| `main_candidates`     | `"Puuid"`            | the scoring queue points at identifiers Riot no longer knows       |
| `seed_requests`       | `"ResolvedPuuid"`    | resolved seeds keep a dead identifier                              |

This was learned the hard way and written down in #788; the recovery mechanism
is #789.

**The only recovery path is the Riot ID.** `riot_accounts` stores
`"GameName"` / `"TagLine"`, which are *not* encrypted, and account-v1
`by-riot-id` re-resolves them into a PUUID under the new key. An account with
no Riot ID stored has no way back — it can only be deleted and rediscovered.
That is why the pre-flight gate below is a hard gate and not a warning.

Two pieces of good news worth knowing before panicking:

- **Public URLs do not break.** Player pages are addressed by `nameTag`
  (`/truemains/{nameTag}/…`), never by PUUID, so a switch does not invalidate a
  single indexed URL.
- **`match_participants` also carries `"RiotAccountId"`**, a foreign key to
  `riot_accounts."Id"` (a local `Guid`). That link survives the switch
  untouched. It is the anchor used by the verification queries below, and the
  reason historical match data is recoverable at all.

## Where the key lives

`RIOT_API_KEY` is read from the environment by both compose stacks
(`Riot__ApiKey: ${RIOT_API_KEY}` in `compose.prod.yaml` and
`compose.preprod.yaml`). It exists in **two** places per environment, and both
must be updated together:

| Environment | On the VPS                    | In GitHub                        |
| ----------- | ----------------------------- | -------------------------------- |
| prod        | `/docker/truemain/.env`       | secret `PROD_ENV_FILE`           |
| preprod     | `/docker/truemain-preprod/.env` | secret `PREPROD_ENV_FILE`      |

The deploy jobs push the secret's contents to the VPS project environment
(`deploy-prod.yml`, `deploy-preprod.yml`), **overwriting the file on the host**.
Editing only the `.env` works until the next deploy, which then silently
restores the old key and takes ingestion down with an authentication error that
looks nothing like "somebody forgot a secret". Update the GitHub secret first,
then the `.env`, and never one without the other.

Prod and preprod hold **different keys** by design (`docs/prod.md`,
`docs/preprod.md`): the key and the database are an inseparable pair, so the
two environments can never share one.

## Pre-flight

### 1. Every account must carry a Riot ID

The gate. `missing_riot_id` must be **zero** before anything else happens:

```sql
SELECT
    count(*)                                                   AS total_active,
    count(*) FILTER (
        WHERE "GameName" = ''
           OR "TagLine" IS NULL
           OR "TagLine" = ''
    )                                                          AS missing_riot_id
FROM riot_accounts
WHERE "Status" = 0;  -- RiotAccountStatus.Active
```

Columns are PascalCase and therefore **must be double-quoted** — an unquoted
`gamename` is a "column does not exist" error, not a subtly wrong count.

Broken down by platform, to see whether a gap is regional (which changes how
long the backfill takes, since it is rate-limited per routing value):

```sql
SELECT
    "PlatformId",
    count(*)                                                   AS total,
    count(*) FILTER (WHERE "GameName" = '' OR "TagLine" IS NULL OR "TagLine" = '')
                                                               AS missing_riot_id
FROM riot_accounts
WHERE "Status" = 0
GROUP BY "PlatformId"
ORDER BY missing_riot_id DESC;
```

If the count is not zero, do **not** proceed. Let `AccountRefresh` run until it
is: the process resolves each account through account-v1 `by-puuid`, which
returns `gameName`/`tagLine`, and stamps them on the row (#789). Running the
ingestor with `INGESTOR_JOB_MODE=AccountRefreshOnly` concentrates the whole
call budget on that backfill. Historically ~23 % of prod accounts had no Riot
ID, so budget days, not minutes, the first time.

`"Status" = 1` (`Invalid`) rows are deliberately out of scope: they already
stopped resolving and are excluded from every selection. They will not be
recovered by the switch and do not need to be.

### 2. Snapshot the database

Take a dump *before* the key changes, on the host running the stack:

```bash
cd /docker/truemain
set -a && . ./.env && set +a          # POSTGRES_USER / POSTGRES_DB live in the stack's .env
docker exec -t truemain-postgres \
    pg_dump -U "$POSTGRES_USER" -Fc "$POSTGRES_DB" \
    > /root/truemain-pre-keyswitch-$(date +%Y%m%d).dump
```

The SQL in this document is meant to be run the same way:

```bash
docker exec -it truemain-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"
```

Copy it off the VPS. This is the rollback path for everything below, and it is
only useful together with the **old key** — restoring the dump under the new key
gives back a database of identifiers the new key cannot resolve. Keep the old
key string somewhere safe until the switch is confirmed good; a revoked or
regenerated key makes the rollback unusable.

### 3. Know the regional routing

Re-resolution goes through account-v1, which is served on **regional** hosts,
not platform hosts. `Core/RiotRouting.cs` owns the mapping:

| Regional route | Platforms                          |
| -------------- | ---------------------------------- |
| `americas`     | NA1, BR1, LA1, LA2, OC1            |
| `europe`       | EUW1, EUN1, RU, TR1                |
| `asia`         | KR, JP1                            |
| `sea`          | PH2, SG2, TH2, TW2, VN2            |

**account-v1 does not serve the `sea` host.** `RiotRouting.ToRegional` maps the
SEA platforms to `RegionalRoute.Sea` because match-v5 does serve them, so a
re-resolution of a SEA account must be routed to **`asia`** instead. Prod
currently stores no SEA accounts (EUW/NA/KR dominate — see #1149), so this is a
caveat to check rather than a step to perform; if any SEA row exists at switch
time, route it to `asia` explicitly.

Rate limits are **per routing value**, so the re-resolution of `europe`,
`americas` and `asia` proceeds independently and in parallel; the slowest
region sets the total duration.

## The switch

### 4. Freeze ingestion

Nothing may write a PUUID while the key is in flux. Stop the ingestor — there is
no "off" `JobMode`, so stopping the container is the freeze:

```bash
cd /docker/truemain
docker compose stop ingestor
docker compose ps
```

Leave the Api up: reads keep serving from the database, which is still
internally consistent. Only calls to Riot are affected.

### 5. Swap the key

1. Update the GitHub secret (`PROD_ENV_FILE` / `PREPROD_ENV_FILE`) — the whole
   file, with `RIOT_API_KEY=` carrying the new value.
2. Update `/docker/truemain/.env` (or `/docker/truemain-preprod/.env`) to match.
3. Restart the API so it picks up the new value, and keep the ingestor down for
   now:

```bash
docker compose up -d --force-recreate api
```

### 6. Re-resolve the PUUIDs

The re-resolution job already exists: `AccountRefreshProcess` resolves each
account by PUUID and, on a `404`, falls back to account-v1 `by-riot-id` with the
stored Riot ID, writes the new PUUID onto the row and stamps
`LastProfileSyncAtUtc` (`TryRecoverByRiotIdAsync`). Under a new key *every*
account takes that path, so the switch is simply that process run over the whole
table:

```bash
cd /docker/truemain
INGESTOR_JOB_MODE=AccountRefreshOnly docker compose up -d ingestor
docker compose logs -f ingestor
```

Watch the per-cycle summary line — `profileRecovered` should account for
essentially the whole selection, `profileInvalidated` for a small tail of
genuinely dead accounts:

```
Account refresh summary: selected=…, profileRecovered=…, profileInvalidated=…, profileFailed=…
```

Notes on the mechanics that matter here:

- Batches are sized by `AccountRefresh:BatchSize` and every call goes through
  the shared rate limiter, so the job self-paces; do not try to parallelise it
  by hand.
- `profileFailed` is *not* an invalidation — a transport, auth or 429 failure
  leaves the row active for the next cycle. A wall of `profileFailed` right
  after the swap almost always means the key itself is wrong (401/403); check
  before letting the job keep running.
- A recovered PUUID that already belongs to another row invalidates the stale
  duplicate rather than colliding on the unique index (#1223). A handful of
  those is normal.

Progress, at any moment:

```sql
SELECT
    count(*) FILTER (WHERE "LastProfileSyncAtUtc" >= :switch_started_utc) AS resolved,
    count(*) FILTER (WHERE "LastProfileSyncAtUtc" <  :switch_started_utc
                        OR "LastProfileSyncAtUtc" IS NULL)               AS pending,
    count(*) FILTER (WHERE "Status" = 1)                                 AS invalid
FROM riot_accounts;
```

### 7. Verify linkage

Only once `pending` is zero.

Accounts still holding a pre-switch PUUID — must be zero (excluding `Invalid`):

```sql
SELECT count(*)
FROM riot_accounts
WHERE "Status" = 0
  AND ("LastProfileSyncAtUtc" IS NULL OR "LastProfileSyncAtUtc" < :switch_started_utc);
```

Historical participants that no longer join their account by PUUID. The
`"RiotAccountId"` foreign key is what makes this measurable — it never moved:

```sql
SELECT count(*) AS orphaned_participants
FROM match_participants mp
JOIN riot_accounts ra ON ra."Id" = mp."RiotAccountId"
WHERE mp."RiotAccountId" IS NOT NULL
  AND mp."Puuid" <> ra."Puuid";
```

A non-zero count here is expected, not a failure: it is exactly the set of rows
whose `"Puuid"` column still carries the old identifier. Repair them from the
foreign key, which is the authoritative link:

```sql
UPDATE match_participants mp
SET "Puuid" = ra."Puuid"
FROM riot_accounts ra
WHERE ra."Id" = mp."RiotAccountId"
  AND mp."Puuid" <> ra."Puuid";
```

Run it in batches on a large table (`… AND mp."Id" IN (SELECT … LIMIT 50000)`)
rather than as one statement — a single transaction over the whole participant
table holds locks far too long.

Mains have **no** foreign key to `riot_accounts`; they are keyed by
`("PlatformId", "Puuid", "ChampionId")`, so they cannot be repaired from a link
that does not exist. Measure the damage:

```sql
SELECT count(*) AS orphaned_mains
FROM main_champion_stats mcs
LEFT JOIN riot_accounts ra
       ON ra."Puuid" = mcs."Puuid"
      AND ra."PlatformId" = mcs."PlatformId"
WHERE ra."Id" IS NULL;
```

Two options, in order of preference:

1. **Re-key from the participants** if the repair above ran first — the
   `(PlatformId, Puuid)` pairs in `main_champion_stats` map one-to-one onto the
   accounts, so an `UPDATE … FROM riot_accounts` joined on the *old* PUUID (kept
   in the dump) restores them.
2. **Recompute** by letting `MainAnalysis` run
   (`INGESTOR_JOB_MODE=MainAnalysisOnly`). Slower, no dump needed, and it also
   re-derives `main_candidates`, which nothing else repairs.

`main_candidates` and `seed_requests` are queues, not history: it is legitimate
to let them be recomputed rather than repaired.

### 8. Unfreeze

```bash
cd /docker/truemain
docker compose up -d --force-recreate ingestor   # back to INGESTOR_JOB_MODE=Full
```

Then watch, in the admin portal: the Riot API usage panel (429 rate, calls per
minute), pipeline health, and matches ingested per day. Give it a full day
before declaring the switch done — a key that authenticates is not yet a key
that ingests.

## Rehearsal on preprod

The prod switch is performed **only after** the same sequence has been rehearsed
end to end on preprod. Preprod normally runs a tiny, disposable database, which
rehearses nothing: the point of the rehearsal is the volume and the shape of
prod's account table.

1. Take a prod dump (step 2) and restore it into the preprod Postgres. Restore
   `riot_accounts` at minimum; `match_participants` makes the linkage
   verification meaningful and is worth the disk if there is room.
2. Note that those rows carry **prod-key** PUUIDs, which the preprod key cannot
   resolve either — which is precisely the situation being rehearsed. Every
   account goes through `by-riot-id` exactly as it will on prod.
3. Run steps 4 to 8 against preprod, timing the re-resolution per region. That
   number, scaled by the account count, is the outage window to plan for on
   prod.
4. Wipe preprod afterwards and let it rebuild its own small database — a
   preprod carrying a copy of prod's accounts will otherwise burn the preprod
   key's budget on accounts nobody is looking at.

Do not rehearse by pointing preprod at the *new* production key: a key is bound
to one environment's database, and having both stacks on the same key makes the
two rate-limit budgets one budget.

## Rollback

Before step 6 has written anything (i.e. up to and including the swap):

1. Restore the old `RIOT_API_KEY` in both the GitHub secret and the `.env`.
2. `docker compose up -d --force-recreate api ingestor`.

Nothing was lost — no PUUID was rewritten.

Once re-resolution has started, rows carry a mix of old and new PUUIDs and there
is no in-place way back. The rollback is the dump from step 2 restored under the
**old** key:

```bash
docker compose stop api ingestor
docker exec -i truemain-postgres \
    pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists \
    < /root/truemain-pre-keyswitch-YYYYMMDD.dump
# restore the old RIOT_API_KEY in the GitHub secret and the .env, then:
docker compose up -d --force-recreate api ingestor
```

Everything ingested between the dump and the rollback is lost. That is the cost
of the rollback and the reason the switch is rehearsed first and started at a
quiet hour.

## Related

- #788 — PUUIDs are key-scoped; the incident behind this document.
- #789 — the Riot ID backfill that makes recovery possible.
- #1363 — production key application; the switch this runbook was written for.
- [`docs/riot-production-key-application.md`](riot-production-key-application.md)
  — the checklist to clear before applying.
- [`docs/prod.md`](prod.md), [`docs/preprod.md`](preprod.md) — the two stacks,
  their env files and their deploy paths.
