using Microsoft.Data.Sqlite;

namespace ClipboardManager.Data;

/// <summary>
/// 数据库迁移执行器：基于 schema_version 表顺序执行未应用的迁移（规范 §16）。
/// </summary>
public sealed class MigrationRunner
{
    public void Migrate(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS schema_version (
                    version INTEGER NOT NULL,
                    applied_at TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        var appliedVersions = new HashSet<int>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT version FROM schema_version";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                appliedVersions.Add(reader.GetInt32(0));
            }
        }

        foreach (var migration in SqliteSchema.Migrations
                     .Where(m => !appliedVersions.Contains(m.Version))
                     .OrderBy(m => m.Version))
        {
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = migration.Sql;
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO schema_version (version, applied_at) VALUES (@version, @appliedAt)";
                command.Parameters.AddWithValue("@version", migration.Version);
                command.Parameters.AddWithValue("@appliedAt", DateTime.Now.ToString("o"));
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }
}
