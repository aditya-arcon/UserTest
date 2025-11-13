using System;
using System.Linq;
using IDV_Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace UserTest.Infra
{
    /// <summary>
    /// Test-only DbContext that normalizes provider-specific bits (MySQL/SQL Server) so SQLite can build the schema.
    /// - Translates incompatible column types to SQLite affinities
    /// - Normalizes DefaultValueSql (e.g., CURRENT_TIMESTAMP(6) -> CURRENT_TIMESTAMP)
    /// </summary>
    internal sealed class SqliteFriendlyApplicationDbContext : ApplicationDbContext
    {
        public SqliteFriendlyApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            if (!Database.IsSqlite()) return;

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var entityBuilder = modelBuilder.Entity(entityType.ClrType);

                foreach (var prop in entityType.GetProperties())
                {
                    // 1) Fix DefaultValueSql that SQLite doesn't understand
                    var defaultSql = prop.GetDefaultValueSql();
                    if (!string.IsNullOrWhiteSpace(defaultSql))
                    {
                        var normalized = NormalizeDefaultValueSql(defaultSql);
                        if (!string.Equals(defaultSql, normalized, StringComparison.Ordinal))
                        {
                            entityBuilder.Property(prop.Name).HasDefaultValueSql(normalized);
                        }
                    }

                    // 2) Translate provider-specific column types to SQLite affinities
                    var colType = prop.GetColumnType();
                    if (!string.IsNullOrWhiteSpace(colType))
                    {
                        var translated = TranslateToSqliteAffinity(colType!);
                        if (!string.Equals(colType, translated, StringComparison.Ordinal))
                        {
                            entityBuilder.Property(prop.Name).HasColumnType(translated);
                        }
                    }
                }
            }
        }

        private static string NormalizeDefaultValueSql(string sql)
        {
            // Common offenders from MySQL/SQL Server mappings
            var s = sql;

            // CURRENT_TIMESTAMP(6) / CURRENT_TIMESTAMP() -> CURRENT_TIMESTAMP
            s = s.Replace("CURRENT_TIMESTAMP(6)", "CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase)
                 .Replace("CURRENT_TIMESTAMP()", "CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase);

            // Some providers embed "ON UPDATE CURRENT_TIMESTAMP(...)" alongside defaults; drop the ON UPDATE part if present.
            var idx = s.IndexOf("ON UPDATE", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) s = s.Substring(0, idx).Trim();

            return s.Trim();
        }

        private static string TranslateToSqliteAffinity(string providerColumnType)
        {
            var t = providerColumnType.Trim().ToLowerInvariant();

            // Strip any CHARACTER SET / COLLATE suffixes first
            // e.g. "varchar(255) character set utf8mb4" -> "varchar(255)"
            var charSetIdx = t.IndexOf("character set", StringComparison.OrdinalIgnoreCase);
            if (charSetIdx >= 0) t = t[..charSetIdx].Trim();

            var collateIdx = t.IndexOf("collate", StringComparison.OrdinalIgnoreCase);
            if (collateIdx >= 0) t = t[..collateIdx].Trim();

            // Map common MySQL/SQL Server types to SQLite storage classes
            // Ref: https://www.sqlite.org/datatype3.html
            if (t.Contains("char") || t.Contains("varchar") || t.Contains("nvarchar") || t.Contains("nchar")
                || t.Contains("text") || t.Contains("longtext") || t.Contains("mediumtext") || t.Contains("tinytext")
                || t.Contains("json"))
            {
                return "TEXT";
            }

            if (t.Contains("binary") || t.Contains("varbinary") || t.Contains("blob"))
            {
                return "BLOB";
            }

            if (t.Contains("bigint") || t.Contains("int") || t.Contains("tinyint") || t.Contains("smallint"))
            {
                // tinyint(1) is often used as boolean in MySQL – INTEGER is fine for SQLite
                return "INTEGER";
            }

            if (t.Contains("decimal") || t.Contains("numeric") || t.Contains("money") || t.Contains("smallmoney"))
            {
                // NUMERIC keeps precision semantics in SQLite
                return "NUMERIC";
            }

            if (t.Contains("float") || t.Contains("double") || t.Contains("real"))
            {
                return "REAL";
            }

            if (t.Contains("datetime") || t.Contains("timestamp") || t.Contains("date") || t.Contains("time"))
            {
                // Let EF handle conversion; TEXT plays well as ISO-8601 strings in SQLite
                return "TEXT";
            }

            // Fallback: if it had "character" anywhere, make it TEXT; otherwise leave as-is.
            if (t.Contains("character"))
                return "TEXT";

            return providerColumnType; // unchanged if already OK
        }
    }
}
