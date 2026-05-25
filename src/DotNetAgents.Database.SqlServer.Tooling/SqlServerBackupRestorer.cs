using System.Text;
using Microsoft.Data.SqlClient;

namespace DotNetAgents.Database.SqlServer.Tooling;

/// <summary>
/// Restores a SQL Server .bak that is already visible to the engine (e.g. under <c>/var/opt/mssql/backup</c>)
/// using dynamic <c>MOVE</c> clauses so files land under the requested relocation root.
/// </summary>
public static class SqlServerBackupRestorer
{
    /// <summary>
    /// Drops (if exists) and restores <paramref name="databaseName"/> from <paramref name="backupPathOnServer"/>.
    /// </summary>
    /// <param name="connectionString">Connection to <c>master</c> (or any DB) with permission to RESTORE.</param>
    /// <param name="databaseName">Target database name.</param>
    /// <param name="backupPathOnServer">Path as seen inside SQL Server (e.g. <c>/var/opt/mssql/backup/MyDb.bak</c>).</param>
    /// <param name="relocateDataRoot">Directory for relocated data/log files (must exist and be writable by SQL Server).</param>
    /// <param name="cancellationToken">Cancellation token for the restore operation.</param>
    public static async Task RestoreFromBackupFileAsync(
        string connectionString,
        string databaseName,
        string backupPathOnServer,
        string relocateDataRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPathOnServer);
        ArgumentException.ThrowIfNullOrWhiteSpace(relocateDataRoot);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await DropDatabaseIfExistsAsync(connection, databaseName, cancellationToken).ConfigureAwait(false);

        var moveClauses = await BuildMoveClausesAsync(connection, databaseName, backupPathOnServer, relocateDataRoot, cancellationToken)
            .ConfigureAwait(false);

        var restoreSql = new StringBuilder();
        restoreSql.Append("RESTORE DATABASE [").Append(EscapeIdentifier(databaseName)).Append("] ");
        restoreSql.Append("FROM DISK = @pBackup WITH ");
        restoreSql.Append(moveClauses);
        restoreSql.Append(", REPLACE, RECOVERY");

        await using var restoreCmd = new SqlCommand(restoreSql.ToString(), connection);
        restoreCmd.Parameters.AddWithValue("@pBackup", backupPathOnServer);
        restoreCmd.CommandTimeout = 0;
        await restoreCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DropDatabaseIfExistsAsync(SqlConnection connection, string databaseName, CancellationToken cancellationToken)
    {
        var sql = $"""
            IF DB_ID(@name) IS NOT NULL
            BEGIN
              ALTER DATABASE [{EscapeIdentifier(databaseName)}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
              DROP DATABASE [{EscapeIdentifier(databaseName)}];
            END
            """;
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@name", databaseName);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> BuildMoveClausesAsync(
        SqlConnection connection,
        string databaseName,
        string backupPathOnServer,
        string relocateDataRoot,
        CancellationToken cancellationToken)
    {
        var files = new List<(string LogicalName, char Type)>();
        await using (var cmd = new SqlCommand("RESTORE FILELISTONLY FROM DISK = @pBackup;", connection))
        {
            cmd.Parameters.AddWithValue("@pBackup", backupPathOnServer);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var logical = reader.GetString(0);
                var type = reader.GetString(2)[0];
                files.Add((logical, type));
            }
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException($"RESTORE FILELISTONLY returned no files for '{backupPathOnServer}'.");
        }

        var root = relocateDataRoot.TrimEnd('/');
        var sb = new StringBuilder();
        foreach (var (logical, type) in files)
        {
            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            var ext = type switch
            {
                'D' => ".mdf",
                'L' => ".ldf",
                _ => ".ndf"
            };

            var safeLogical = SanitizeFileToken(logical);
            var dest = $"{root}/{databaseName}_{safeLogical}{ext}";
            sb.Append("MOVE N'").Append(EscapeStringLiteral(logical)).Append("' TO N'").Append(EscapeStringLiteral(dest)).Append('\'');
        }

        return sb.ToString();
    }

    private static string SanitizeFileToken(string logicalName)
    {
        var chars = logicalName.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] is not ('-' or '_' or '.'))
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private static string EscapeIdentifier(string name) => name.Replace("]", "]]");

    private static string EscapeStringLiteral(string value) => value.Replace("'", "''");

    /// <summary>
    /// Returns approximate row counts per user table (heap or clustered index) for parity checks after ETL.
    /// </summary>
    public static async Task<IReadOnlyDictionary<(string Schema, string Table), long>> GetUserTableRowCountsAsync(
        string connectionString,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
            SELECT s.name AS schema_name, t.name AS table_name, SUM(p.rows) AS row_count
            FROM [{EscapeIdentifier(databaseName)}].sys.tables t
            JOIN [{EscapeIdentifier(databaseName)}].sys.schemas s ON t.schema_id = s.schema_id
            JOIN [{EscapeIdentifier(databaseName)}].sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
            GROUP BY s.name, t.name
            ORDER BY s.name, t.name;
            """;

        await using var cmd = new SqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var dict = new Dictionary<(string, string), long>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            var rows = Convert.ToInt64(reader.GetValue(2));
            dict[(schema, table)] = rows;
        }

        return dict;
    }
}
