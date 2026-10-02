# Database Migrations

How to create, apply, inspect, and roll back EF Core migrations in LedgerLite.

## Where things live

| Item                                  | Location                                                             |
| ------------------------------------- | -------------------------------------------------------------------- |
| DbContext                             | `LedgerLite.Data/LedgerDbContext.cs`                                 |
| Entity configurations                 | `LedgerLite.Data/Configurations/`                                    |
| Migrations                            | `LedgerLite.Data/Migrations/`                                        |
| Startup project (used by `dotnet ef`) | `LedgerLite.Api`                                                     |
| Connection string                     | `LedgerLite.Api/appsettings.json` under `ConnectionStrings:LedgerDb` |
| Tool manifest                         | `.config/dotnet-tools.json` (repo root)                              |

Every `dotnet ef` command uses the same two flags:

```
-p LedgerLite.Data     # project that contains the DbContext and migrations
-s LedgerLite.Api      # startup project that supplies configuration and DI
```

Run all commands from the **repo root** (the folder containing `LedgerLite.sln` and `.config`).

## One-time setup

`dotnet-ef` is installed as a **local tool**, recorded in the repo, not globally.

```bash
dotnet tool restore
```

Verify:

```bash
dotnet ef --version
```

The tool's major version should match the `Microsoft.EntityFrameworkCore.*` package versions. Check with:

```bash
dotnet list LedgerLite.Data package
```

### Database prerequisites

SQL Server must be reachable using the connection string in `appsettings.json`.

- **Windows:** LocalDB works out of the box with `Server=(localdb)\MSSQLLocalDB;...`
- **macOS / Linux:** run SQL Server in Docker and point the connection string at `localhost,1433`.

Example Docker command:

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<YourStrong!Passw0rd>" \
  -p 1433:1433 --name ledgerlite-sql -d mcr.microsoft.com/mssql/server:2022-latest
```

Do not commit real passwords. Use [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) for non-LocalDB connection strings:

```bash
dotnet user-secrets init --project LedgerLite.Api
dotnet user-secrets set "ConnectionStrings:LedgerDb" "<your connection string>" --project LedgerLite.Api
```

## Everyday commands

### Create a migration

After changing a domain entity or its configuration:

```bash
dotnet build
dotnet ef migrations add <MigrationName> -p LedgerLite.Data -s LedgerLite.Api
```

Use descriptive PascalCase names: `InitialCreate`, `BlockLedgerMutation`, `AddLoanTables`.

**Always open the generated file and read it** before applying. Check that it does what you intended, especially column types (`decimal(18,2)`), `onDelete: ReferentialAction.Restrict`, and index filters.

### Apply migrations

Apply everything pending:

```bash
dotnet ef database update -p LedgerLite.Data -s LedgerLite.Api
```

Apply up to a specific migration:

```bash
dotnet ef database update <MigrationName> -p LedgerLite.Data -s LedgerLite.Api
```

### List migrations

Shows every migration and whether it has been applied:

```bash
dotnet ef migrations list -p LedgerLite.Data -s LedgerLite.Api
```

### Roll back

Roll the database back to an earlier migration (this runs the `Down` methods of everything after it):

```bash
dotnet ef database update <PreviousMigrationName> -p LedgerLite.Data -s LedgerLite.Api
```

Roll back everything (empty database, no tables except the history table):

```bash
dotnet ef database update 0 -p LedgerLite.Data -s LedgerLite.Api
```

### Remove the latest migration

Only for a migration that has **not been applied** (or that you've rolled back first). It deletes the migration files and updates the model snapshot:

```bash
dotnet ef migrations remove -p LedgerLite.Data -s LedgerLite.Api
```

### Generate a SQL script

Useful for review, or for running against a database you don't connect to directly:

```bash
dotnet ef migrations script --idempotent -o migrate.sql -p LedgerLite.Data -s LedgerLite.Api
```

`--idempotent` produces a script that checks the history table and only runs what's missing, so it's safe to run more than once.

### Reset the local database

Destroys all data in the database named in the connection string. Local development only.

```bash
dotnet ef database drop --force -p LedgerLite.Data -s LedgerLite.Api
dotnet ef database update -p LedgerLite.Data -s LedgerLite.Api
```

### Check what EF is connected to

```bash
dotnet ef dbcontext info -p LedgerLite.Data -s LedgerLite.Api
```

## Workflow checklist

1. Change the domain entity and/or configuration.
2. `dotnet build`
3. `dotnet ef migrations add <Name> ...`
4. Read the generated migration file.
5. `dotnet ef database update ...`
6. Run the tests.
7. Commit the migration files **and** `LedgerDbContextModelSnapshot.cs` together.

## Rules

- **Never edit a migration that has already been applied** (to any shared database). Add a new migration instead.
- **Never delete a migration file by hand.** Use `migrations remove` so the snapshot stays in sync.
- **Always commit the model snapshot** (`LedgerDbContextModelSnapshot.cs`) with the migration. If it's missing or stale, the next migration will be wrong.
- **One logical change per migration.** It keeps rollbacks and reviews simple.
- **Don't run `database update` against production from a laptop.** Generate an idempotent script and review it.

## Special case: the immutability triggers

The `BlockLedgerMutation` migration installs `INSTEAD OF UPDATE, DELETE` triggers on `JournalLines` and `JournalEntries`.

- Schema changes (new tables, new columns, new indexes) are unaffected.
- A migration that must **update or delete existing ledger rows** (a data backfill, for example) will fail with _"Journal lines are immutable"_. In that migration, drop the trigger, run the data change, then recreate the trigger:

```csharp
migrationBuilder.Sql("DROP TRIGGER TR_JournalLines_Immutable");
// ... data change here ...
migrationBuilder.Sql(@"
CREATE TRIGGER TR_JournalLines_Immutable ON JournalLines
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 50001, 'Journal lines are immutable. Post a reversing entry instead.', 1;
END");
```

- Rolling back past `BlockLedgerMutation` runs its `Down` method, which drops both triggers.

## Troubleshooting

| Symptom                                                                                   | Likely cause                                                             | Fix                                                                                                                                  |
| ----------------------------------------------------------------------------------------- | ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| `Could not execute because the specified command or file was not found` (for `dotnet ef`) | Local tool not restored, or command run outside the repo                 | Run `dotnet tool restore` from the repo root                                                                                         |
| `Unable to create a 'DbContext' of type ...`                                              | Startup project can't build, or config is missing                        | Run `dotnet build`, confirm `LedgerLite.Api` references `LedgerLite.Data` and has the `Microsoft.EntityFrameworkCore.Design` package |
| `The ConnectionString property has not been initialized`                                  | Connection string missing or wrong key                                   | Check `ConnectionStrings:LedgerDb` in `appsettings.json` or user secrets                                                             |
| `A network-related or instance-specific error occurred`                                   | SQL Server not running or wrong server name                              | Start LocalDB (`sqllocaldb start MSSQLLocalDB`) or the Docker container, then retry                                                  |
| `Login failed for user`                                                                   | Wrong credentials                                                        | Check the connection string or user secrets                                                                                          |
| Warning: tool version is older than the runtime                                           | `dotnet-ef` and EF Core packages differ in major version                 | `dotnet tool update dotnet-ef --version <matching version>`                                                                          |
| `The model for context 'LedgerDbContext' has pending changes`                             | Entities changed without a new migration                                 | Create a migration with `migrations add`                                                                                             |
| `There is already an object named 'X' in the database`                                    | Database was created outside migrations, or history table is out of sync | Reset the local database (see above)                                                                                                 |
| `Journal lines are immutable` during an update                                            | A migration is modifying ledger rows                                     | See the trigger section above                                                                                                        |
| Seed data changes aren't showing up                                                       | `HasData` changes only apply through a new migration                     | Add a migration, then `database update`                                                                                              |

## Quick reference

```bash
dotnet tool restore                                                       # once per clone
dotnet ef migrations add <Name>   -p LedgerLite.Data -s LedgerLite.Api    # create
dotnet ef database update         -p LedgerLite.Data -s LedgerLite.Api    # apply all
dotnet ef database update <Name>  -p LedgerLite.Data -s LedgerLite.Api    # apply or roll back to <Name>
dotnet ef database update 0       -p LedgerLite.Data -s LedgerLite.Api    # roll back everything
dotnet ef migrations list         -p LedgerLite.Data -s LedgerLite.Api    # status
dotnet ef migrations remove       -p LedgerLite.Data -s LedgerLite.Api    # delete latest unapplied
dotnet ef migrations script --idempotent -o migrate.sql -p LedgerLite.Data -s LedgerLite.Api
dotnet ef database drop --force   -p LedgerLite.Data -s LedgerLite.Api    # local only
```
