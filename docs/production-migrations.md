# Applying database migrations in production

## TL;DR

- **Development / single-instance:** the host applies pending EF Core
  migrations at startup. This is gated by
  `Database:ApplyMigrationsOnStartup` (env var
  `Database__ApplyMigrationsOnStartup`), which is `true` in the dev compose
  files and in `appsettings.Development.json`.
- **Preprod and production:** an **idempotent SQL script** generated from the
  migrations is applied as a discrete deployment step, by the
  `migrate` job of `rollout.yml`, called by `deploy-preprod.yml`/`deploy-prod.yml`,
  before the new images roll. `Database__ApplyMigrationsOnStartup` is `false`
  in both `compose.preprod.yaml` and `compose.prod.yaml`.

This doc covers issue #246. It documents the production path; the startup
behaviour itself is already gated by `DatabaseOptions.ApplyMigrationsOnStartup`
(see `backend/Data/DatabaseMigrator.cs`).

## Why not migrate at startup in production

`Database.MigrateAsync()` at process startup is convenient for local work but
[Microsoft advises against it for production][applying]:

1. **Server-farm concurrency.** When more than one instance of the API or
   Ingestor starts at once, each may try to apply the same migration
   concurrently. EF Core 9+ adds a migration lock that prevents corruption, but
   instances still contend and a losing instance can fail its startup.
2. **Elevated privileges.** The application would need schema-altering rights on
   the database at runtime. Production app accounts should be limited to
   read/write on existing objects.
3. **No review / no controlled rollback.** SQL is applied directly with no
   chance to inspect it first, and there is no first-class rollback path. A
   migration that intends to rename a column may instead drop it.

References:

- EF Core — [Applying Migrations][applying]
- ASP.NET Core — [Applying migrations in production][aspnet]

## Recommended path: idempotent SQL script

EF Core can emit an **idempotent** script that checks the
`__EFMigrationsHistory` table at runtime and applies only the migrations that
are missing. The same script can be run against a database at any migration
state, which makes it safe for environments where you do not know the exact
current migration.

The `Data` project ships an `IDesignTimeDbContextFactory`
(`backend/Data/DesignTimeDbContextFactory.cs`), so the EF tooling can target it
directly without booting the API or Ingestor host.

### Generate the script

From `backend/`:

```bash
dotnet tool restore   # if the EF tool is managed as a local tool
dotnet ef migrations script \
  --idempotent \
  --project Data/Data.csproj \
  --startup-project Data/Data.csproj \
  --output artifacts/migrations/truemain.sql
```

- `--idempotent` makes the script safe to run regardless of the database's
  current migration state.
- `dotnet ef migrations script` has no `--connection` flag (unlike `database
  update`/`dbcontext scaffold`) — `DesignTimeDbContextFactory` still needs a
  syntactically valid connection string to build against, so on a runner with
  no real database reachable, supply one via the `ConnectionStrings__TrueMain`
  environment variable instead — `DesignTimeDbContextFactory` layers
  appsettings/user-secrets/environment, with environment variables taking
  precedence, so this doesn't require touching any file. Generating the
  script does **not** require a reachable database — the string never has to
  resolve to anything real.

Always open and review the generated SQL before it applies — confirm there
are no unintended `DROP`s and no destructive data operations. The
`migrate-preprod`/`migrate-prod` jobs print the full script to the job log
(`::group::migration.sql`) and upload it as a build artifact (90-day
retention) before applying it, so both runs leave an auditable record —
see "CI wiring" below.

### Apply the script

The general shape, if you had a direct connection string to the target
database:

```bash
psql "$PRODUCTION_CONNECTION_STRING" --single-transaction -f truemain.sql
```

`--single-transaction` rolls the whole script back if any statement fails.

### No non-transactional statements in migrations

This path imposes a hard rule, verified by CI: **no migration may contain a
statement that Postgres refuses inside a transaction** — in practice
`CREATE INDEX CONCURRENTLY` / `DROP INDEX CONCURRENTLY`, `VACUUM`, or
`ALTER TYPE ... ADD VALUE` on older servers.

EF's `migrationBuilder.Sql(..., suppressTransaction: true)` does **not** make
this work here. The flag only has an effect for `Database.MigrateAsync()` at
runtime, and `ApplyMigrationsOnStartup` is permanently `false` in preprod and
prod. On the script path, two things defeat it independently:

1. `--idempotent` wraps **every** statement in a `DO $EF$ BEGIN ... END $EF$;`
   PL/pgSQL block, and Postgres rejects `CONCURRENTLY` inside a function
   (`CREATE INDEX CONCURRENTLY cannot be executed from a function`), whatever
   the surrounding transaction state.
2. `psql --single-transaction` opens an explicit transaction around the whole
   file, so the `COMMIT;` / `START TRANSACTION;` markers EF does emit around
   suppressed statements are not the boundary they look like.

This was silent for a long time (#1227): preprod and prod already carried the
offending migrations in `__EFMigrationsHistory`, so the idempotent guard skipped
their blocks. It would have failed on the first database built from scratch — a
new preprod, a disaster-recovery restore, or onboarding.

Migrations therefore use plain, transactional DDL. Where an index build on a
large hot table is a genuine concern, the trade-off is documented in the
migration itself (see `20260716220811_AddMatchParticipantFullPoolIndex.cs`):
a transactional build is atomic, leaves no `INVALID` index behind on failure,
and — decisively — is fully reproducible from the migrations alone.

The `migrate-fresh` job in `ci.yml` enforces this on every PR: it generates the
idempotent script and applies it with `psql --single-transaction` to a blank
Postgres service container, then re-applies it to assert the script is a no-op
the second time. Any statement incompatible with the deploy path fails the
build instead of surfacing during a disaster recovery.

Neither VPS exposes a connection string reachable from CI (see "CI wiring"
below), so the actual `migrate` job of the rollout pipes the script
into `psql` inside the running container over SSH instead — same
`--single-transaction` flag, different transport.

### A migration that depends on a server setting runs before the setting exists

`migrate-preprod` / `migrate-prod` deliberately run **before** the deploy job rolls
the images, so the script always meets the *previous* server. A migration whose
statement depends on a setting introduced by the same PR therefore runs against a
server that does not have it yet, and — because EF stamps
`__EFMigrationsHistory` regardless — it is never retried.

`20260902141349_EnablePgStatStatements` is the case in point (#1366). It creates the
`pg_stat_statements` extension, which requires
`shared_preload_libraries=pg_stat_statements`, added to the compose files in the same
PR and only effective after the Postgres container restarts. The migration catches
the failure and raises a `NOTICE` rather than breaking the chain, which is the right
behaviour for every database that will never carry the preload (a developer's local
server, the `migrate-fresh` container, a restored dump) — but it does mean the
extension does not appear on its own.

So on each environment, **once**, after the deploy that restarts Postgres:

```bash
docker exec -i <postgres-container> psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  -c 'CREATE EXTENSION IF NOT EXISTS pg_stat_statements;'
docker exec -i <postgres-container> psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  -c "SELECT count(*) FROM pg_stat_statements;"
```

The general rule: when a migration depends on a server setting shipped alongside it,
expect to apply it by hand after the restart, and say so in the PR.

## CI wiring

Both `deploy-preprod.yml` (on every push to `develop`) and `deploy-prod.yml`
(on every published release) run the same sequence: publish images →
apply migrations (`rollout.yml`, job `migrate`) → roll the VPS (job `deploy`). The deploy job depends on the migrate job
succeeding, so a failed or aborted migration blocks the image roll instead of
shipping a schema mismatch. Preprod runs this on every merge to `develop`,
so it is the first place a bad migration script shows up — before it ever
reaches prod.

Both workflows put a `preflight` job in front of everything: it fails the
run when any piece of the deployment configuration (SSH secrets, env file,
Hostinger API key, VM id) is missing.
Deliberately not a green skip: the migration runs first, so a deploy-side
configuration checked at deploy time could only ever skip the image roll
*after* the schema had already moved — the mismatch in the other direction,
old binary against new schema, and just as broken. Now that
`ApplyMigrationsOnStartup` is permanently `false`, both halves have to happen
or neither does.

The migrate job, in each workflow:

1. Runs only once `preflight` has confirmed the SSH secrets exist, so the
   check fires before the images are even published.
2. Restores `Data.csproj` (a plain checkout has no `obj/project.assets.json`
   yet, and `dotnet ef` does not restore on its own), then generates the
   idempotent script from the deployed commit/tag's checkout.
   `dotnet ef migrations script` never opens a connection, but
   `DesignTimeDbContextFactory` still requires a syntactically valid
   connection string to build against — and unlike `database update`/
   `dbcontext scaffold`, `migrations script` has no `--connection` flag — so
   the runner (which has no real database reachable, let alone credentials)
   is given a throwaway one via the `ConnectionStrings__TrueMain` environment
   variable. All of this lives in the `.github/actions/migration-script`
   composite action, shared with the `migrate-fresh` CI job so the script
   validated in CI is generated exactly like the one deployed.

3. Prints the script to the job log and uploads it as a build artifact
   (90-day retention) — an auditable record of exactly what ran, since
   nothing else in this path shows the SQL before it applies.
4. Applies it over SSH by piping it into `psql` inside the already-running
   Postgres container — neither VPS's Postgres has a connection reachable
   from a GitHub-hosted runner (prod: no published port at all; preprod:
   loopback-only). The SSH key is dedicated to CI and locked to a forced
   `command=` on the VPS side (see `docs/preprod.md`/`docs/prod.md`), so the
   workflow doesn't send a remote command at all — whatever it sent would be
   ignored anyway. The host key is pinned via a `PREPROD_SSH_HOST_KEY`/
   `PROD_SSH_HOST_KEY` repo variable rather than trusted fresh from
   `ssh-keyscan` on every run, which would only be TOFU-per-run (no real
   protection, since ephemeral runners never have a prior-trusted
   `known_hosts` to compare against).

The credential used inside the container is the same `POSTGRES_USER` the app
connects with — there is no separate restricted migration-only role today.
Splitting one off is follow-up work, not blocking (#1058).

Not yet done: failing the build when the model has pending changes without a
corresponding migration, via `dotnet ef migrations has-pending-model-changes`.

## Local development

Nothing changes for local work. The dev compose files
(`compose.yaml`, `compose.dev.yaml`) set
`Database__ApplyMigrationsOnStartup: "true"`, and
`appsettings.Development.json` enables it, so the host applies migrations on
startup as before. The migrator now logs when it applies, succeeds, skips
(gating disabled), or fails — see `backend/Data/DatabaseMigrator.cs`.

## Follow-up work

- The migration credential is currently the same `POSTGRES_USER` the app
  connects with, not a dedicated schema-only role — see the "CI wiring"
  section above. Splitting one off needs a Postgres-side role change, tracked
  separately from the CD wiring (#1058).
- `dotnet ef migrations has-pending-model-changes` is not yet wired into CI to
  catch a model change without a matching migration.

[applying]: https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying
[aspnet]: https://learn.microsoft.com/aspnet/core/data/ef-rp/migrations#applying-migrations-in-production
