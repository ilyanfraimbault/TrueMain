# Applying database migrations in production

## TL;DR

- **Development / single-instance:** the host applies pending EF Core
  migrations at startup. This is gated by
  `Database:ApplyMigrationsOnStartup` (env var
  `Database__ApplyMigrationsOnStartup`), which is `true` in the dev compose
  files and in `appsettings.Development.json`.
- **Production:** prefer applying an **idempotent SQL script** generated from
  the migrations as a discrete deployment step, with
  `Database__ApplyMigrationsOnStartup` set to `false`. The script can be
  reviewed, archived, handed to a DBA, and rolled back in a controlled way.

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
- The design-time factory builds its connection from configuration / user
  secrets / environment, or you can pass `--connection "<conn>"`. Generating the
  script does **not** require a reachable database.

Always open and review the generated SQL before it leaves CI — confirm there
are no unintended `DROP`s and no destructive data operations.

### Apply the script

Run the reviewed script against the target database as a deployment step,
**before** the new application image starts:

```bash
psql "$PRODUCTION_CONNECTION_STRING" --single-transaction -f truemain.sql
```

`--single-transaction` rolls the whole script back if any statement fails. Note
that a small number of migration operations cannot run inside a transaction
(for example operations that alter the database itself); EF isolates those into
their own migrations, but keep it in mind when reviewing.

## Suggested CI wiring

The exact CI/deploy mechanics are an owner decision (see the "Open decisions"
section). A workable shape:

1. **Build stage** — generate the idempotent script and publish it as a build
   artifact:

   ```yaml
   - name: Generate idempotent migration script
     working-directory: backend
     run: |
       dotnet ef migrations script --idempotent \
         --project Data/Data.csproj --startup-project Data/Data.csproj \
         --output "$GITHUB_WORKSPACE/artifacts/migrations/truemain.sql"
   - uses: actions/upload-artifact@v4
     with:
       name: migration-script
       path: artifacts/migrations/truemain.sql
   ```

2. **Deploy stage** — download the artifact, apply it against production with a
   restricted migration credential, then roll the application image. Keep
   `Database__ApplyMigrationsOnStartup=false` for the production services so the
   app never migrates on its own.

3. Optionally fail the build when the model has changes without a corresponding
   migration, using `dotnet ef migrations has-pending-model-changes`.

## Local development

Nothing changes for local work. The dev compose files
(`compose.yaml`, `compose.dev.yaml`) set
`Database__ApplyMigrationsOnStartup: "true"`, and
`appsettings.Development.json` enables it, so the host applies migrations on
startup as before. The migrator now logs when it applies, succeeds, skips
(gating disabled), or fails — see `backend/Data/DatabaseMigrator.cs`.

## Open decisions

- **`compose.prod.yaml` currently sets
  `Database__ApplyMigrationsOnStartup: "true"`.** To adopt the script-based path
  above, this must be flipped to `"false"` and the deploy pipeline must apply
  the script before the API/Ingestor start. Flipping it is a deploy-mechanics
  change left to the owner so it can be sequenced with the CI work and with
  issue #208.
- Whether the migration is applied with `psql`, `dotnet ef database update` from
  a controlled runner, or a dedicated migration job is also an owner decision.

[applying]: https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying
[aspnet]: https://learn.microsoft.com/aspnet/core/data/ef-rp/migrations#applying-migrations-in-production
