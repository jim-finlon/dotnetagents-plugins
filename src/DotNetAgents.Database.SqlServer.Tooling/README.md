# DotNetAgents.Database.SqlServer.Tooling

Small helpers for migration lanes that need to:

- Restore `.bak` files into a disposable SQL Server container with automatic `MOVE` relocation (via `RESTORE FILELISTONLY`)
- Capture **approximate row counts** per user table for parity checks against PostgreSQL imports

## Usage

```csharp
await SqlServerBackupRestorer.RestoreFromBackupFileAsync(
    connectionString: "Server=localhost,14336;User Id=sa;Password=***;Trust Server Certificate=True",
    databaseName: "UnityAssetSnippets",
    backupPathOnServer: "/backups/UnityAssetSnippets.bak",
    relocateDataRoot: "/var/opt/mssql/data",
    cancellationToken: ct);

var counts = await SqlServerBackupRestorer.GetUserTableRowCountsAsync(
    connectionString: "Server=localhost,14336;Database=UnityAssetSnippets;User Id=sa;Password=***;Trust Server Certificate=True",
    databaseName: "UnityAssetSnippets",
    cancellationToken: ct);
```

Paths in `backupPathOnServer` / `relocateDataRoot` are **as seen inside the SQL Server container**.

Pair these helpers with your repository's own Docker or PowerShell replay path
when you need repeatable local restore checks.
