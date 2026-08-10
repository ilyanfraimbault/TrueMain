# Applying database migrations in production

## TL;DR

- **Development / single-instance:** the host applies pending EF Core
  migrations at startup. This is gated by
  `Database:ApplyMigrationsOnStartup` (env var
  `Database__ApplyMigrationsOnStartup`), which is `true` in the dev compose
  files and in `appsettings.Development.json`.
- **Preprod and production:** an **idempotent SQL script** generated from the
  migrations is applied as a discrete deployment step, by the
  `migrate-preprod`/`migrate-prod` jobs in `deploy-preprod.yml`/`deploy-prod.yml`,
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

`--single-transaction` rolls the whole script back if any statement fails. Note
that a small number of migration operations cannot run inside a transaction
(for example operations that alter the database itself); EF isolates those into
their own migrations, but keep it in mind when reviewing.

Neither VPS exposes a connection string reachable from CI (see "CI wiring"
below), so the actual `migrate-preprod`/`migrate-prod` jobs pipe the script
into `psql` inside the running container over SSH instead — same
`--single-transaction` flag, different transport.

## CI wiring

Both `deploy-preprod.yml` (on every push to `develop`) and `deploy-prod.yml`
(on every published release) run three jobs in sequence: publish images →
apply migrations → roll the VPS. The deploy job depends on the migrate job
succeeding, so a failed or aborted migration blocks the image roll instead of
shipping a schema mismatch. Preprod runs this on every merge to `develop`,
so it is the first place a bad migration script shows up — before it ever
reaches prod.

The migrate job, in each workflow:

1. Fails immediately if its SSH secrets are missing — deliberately not a
   green skip. Now that `ApplyMigrationsOnStartup` is permanently `false`,
   letting the deploy job proceed without attempting the migration would
   silently roll a new image against a possibly-stale schema, which is
   exactly the failure mode this whole change exists to prevent.
2. Restores `Data.csproj` (a plain checkout has no `obj/project.assets.json`
   yet, and `dotnet ef` does not restore on its own), then generates the
   idempotent script from the deployed commit/tag's checkout.
   `dotnet ef migrations script` never opens a connection, but
   `DesignTimeDbContextFactory` still requires a syntactically valid
   connection string to build against — and unlike `database update`/
   `dbcontext scaffold`, `migrations script` has no `--connection` flag — so
   the runner (which has no real database reachable, let alone credentials)
   is given a throwaway one via the `ConnectionStrings__TrueMain` environment
   variable:

   ```yaml
   - name: Restore Data project
     working-directory: backend
     run: dotnet restore Data/Data.csproj

   - name: Generate idempotent migration script
     working-directory: backend
     env:
       ConnectionStrings__TrueMain: Host=localhost;Port=5432;Database=truemain;Username=truemain;Password=truemain
     run: |
       dotnet ef migrations script --idempotent \
         --project Data/Data.csproj --startup-project Data/Data.csproj \
         --output "$RUNNER_TEMP/migration.sql"
   ```

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
