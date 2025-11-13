// UserTest/Roles/SystemStateServiceConcurrencyTests.cs
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IDV_Backend.Data;
using IDV_Backend.Repositories.SystemStateRepo;
using IDV_Backend.Services.SystemStateSvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace UserTest.Roles;

public class SystemStateServiceConcurrencyTests
{
    private sealed class TestMySqlDb : IAsyncDisposable
    {
        public string ConnectionString { get; }
        public string DatabaseName { get; }
        private readonly string _serverConnString; // same conn without Database=

        public TestMySqlDb(string baseConnString)
        {
            // Parse DB name and build a unique, *short* test DB name
            var (serverConn, baseDbName) = SplitConnAndDb(baseConnString);
            DatabaseName = MakeLockSafeDbName(baseDbName);
            _serverConnString = serverConn;
            ConnectionString = ReplaceDb(baseConnString, DatabaseName);
        }

        public async Task InitializeAsync()
        {
            await ExecSqlAsync(_serverConnString, $"CREATE DATABASE `{DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;");
            await using var db = CreateContext();
            await db.Database.MigrateAsync(); // applies your real migrations
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecSqlAsync(_serverConnString, $"DROP DATABASE IF EXISTS `{DatabaseName}`;");
            }
            catch
            {
                // swallow cleanup exceptions to avoid flaky teardown
            }
        }

        public ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString))
                .EnableDetailedErrors()
                .EnableSensitiveDataLogging()
                .Options;
            return new ApplicationDbContext(options);
        }

        private static async Task ExecSqlAsync(string conn, string sql)
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseMySql(conn, ServerVersion.AutoDetect(conn))
                .Options;

            await using var ctx = new ApplicationDbContext(opts);
            await using var raw = ctx.Database.GetDbConnection();
            await raw.OpenAsync();
            await using var cmd = raw.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        private static (string serverConn, string dbName) SplitConnAndDb(string cs)
        {
            var parts = cs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var dbIdx = parts.FindIndex(p => p.StartsWith("Database=", StringComparison.OrdinalIgnoreCase));
            if (dbIdx < 0) throw new InvalidOperationException("Connection string must include 'Database='.");

            var dbName = parts[dbIdx].Substring("Database=".Length).Trim();
            parts.RemoveAt(dbIdx);
            var serverOnly = string.Join(';', parts) + ";";
            return (serverOnly, dbName);
        }

        private static string ReplaceDb(string cs, string newDb)
        {
            var parts = cs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
                    parts[i] = $"Database={newDb}";
            }
            return string.Join(';', parts);
        }

        // Ensure <DatabaseName> + "_EFMigrationsLock" <= 64 chars
        private static string MakeLockSafeDbName(string baseDb)
        {
            const string lockSuffix = "_EFMigrationsLock";
            const int maxTotal = 64;
            var maxDbLen = maxTotal - lockSuffix.Length; // 47

            // Build a short unique name: <trimmedBase>_t_<8hex>
            var guid8 = Guid.NewGuid().ToString("N")[..8];
            var suffix = "_t_" + guid8;

            // Reserve room for suffix
            var headLen = Math.Max(1, maxDbLen - suffix.Length); // leave at least 1 char
            var head = baseDb.Length > headLen ? baseDb[..headLen] : baseDb;

            // MySQL identifier constraints: keep to [a-zA-Z0-9_]; replace others with '_'
            head = new string(head.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

            var name = head + suffix;

            // Safety clamp (shouldn't hit, but just in case)
            if (name.Length > maxDbLen)
                name = name[..maxDbLen];

            return name;
        }
    }

    private static IConfiguration LoadConfig()
    {
        var basePath = AppContext.BaseDirectory;
        DirectoryInfo? dir = new DirectoryInfo(basePath);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "IDV_Backend")))
            dir = dir.Parent;

        if (dir == null)
            throw new DirectoryNotFoundException("Could not locate solution root containing 'IDV_Backend' folder.");

        var webProjPath = Path.Combine(dir.FullName, "IDV_Backend");

        return new ConfigurationBuilder()
            .SetBasePath(webProjPath)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();
    }

    [Test]
    public async Task Bump_Is_Concurrency_Safe_With_Retry()
    {
        var cfg = LoadConfig();
        var baseConn = cfg.GetConnectionString("DefaultConnection");
        Assert.That(baseConn, Is.Not.Null.And.Not.Empty, "DefaultConnection must be set in appsettings.Development.json");

        await using var dbHelper = new TestMySqlDb(baseConn!);
        await dbHelper.InitializeAsync();

        await using var db1 = dbHelper.CreateContext();
        await using var db2 = dbHelper.CreateContext();

        var repo1 = new SystemStateRepository(db1);
        var repo2 = new SystemStateRepository(db2);

        var svc1 = new SystemStateService(repo1);
        var svc2 = new SystemStateService(repo2);

        var start = await svc1.GetRolesVersionAsync();
        Assert.That(start, Is.EqualTo(1));

        var t1 = svc1.BumpRolesVersionAsync();
        var t2 = svc2.BumpRolesVersionAsync();

        await Task.WhenAll(t1, t2);

        var final = await svc1.GetRolesVersionAsync();
        Assert.That(final, Is.EqualTo(3));
    }
}
