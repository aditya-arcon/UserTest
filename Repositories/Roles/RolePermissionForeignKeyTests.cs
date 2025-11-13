// UserTest/Repositories/Roles/RolePermissionForeignKeyTests_MySql.cs
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySqlConnector; // NuGet: MySqlConnector
using NUnit.Framework;

namespace UserTest.Repositories.Roles
{
    [TestFixture]
    public class RolePermissionForeignKeyTests_MySql
    {
        private string? _serverConn;     // server-level connection (no DB name)
        private string _dbName = null!;
        private string _dbConn = null!;
        private ApplicationDbContext _db = null!;

        [SetUp]
        public void SetUp()
        {
            // 1) Prefer env var; else read appsettings from IDV_Backend
            _serverConn = Environment.GetEnvironmentVariable("MYSQL_TEST_CONN");
            if (string.IsNullOrWhiteSpace(_serverConn))
            {
                _serverConn = TryReadDefaultConnectionFromIdvBackend();
            }

            if (string.IsNullOrWhiteSpace(_serverConn))
            {
                Assert.Ignore("MySQL connection not found via MYSQL_TEST_CONN or IDV_Backend/appsettings.*.json; skipping MySQL FK tests.");
                return;
            }

            _serverConn = StripDatabase(_serverConn!);
            _dbName = "idv_test_" + Guid.NewGuid().ToString("N").Substring(0, 10);

            // 2) Create temp DB
            using (var conn = new MySqlConnection(_serverConn))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE `{_dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
                cmd.ExecuteNonQuery();
            }

            // 3) Use EF Core with Pomelo to create schema + seed
            _dbConn = AddOrReplaceDatabase(_serverConn!, _dbName);

            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseMySql(_dbConn, ServerVersion.AutoDetect(_dbConn))
                .Options;

            _db = new ApplicationDbContext(opts);
            _db.Database.EnsureCreated(); // invokes model + HasData seeds
        }

        [TearDown]
        public void TearDown()
        {
            try { _db?.Dispose(); } catch { /* ignore */ }

            if (!string.IsNullOrWhiteSpace(_serverConn) && !string.IsNullOrWhiteSpace(_dbName))
            {
                try
                {
                    using var conn = new MySqlConnection(_serverConn);
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"DROP DATABASE IF EXISTS `{_dbName}`;";
                    cmd.ExecuteNonQuery();
                }
                catch { /* best-effort */ }
            }
        }

        // ------------------- TESTS -------------------

        [Test]
        public void Insert_With_NonExisting_Role_Or_Permission_Fails()
        {
            if (_db == null) Assert.Ignore("MySQL not configured.");

            // Non-existing RoleId
            _db.RolePermissions.Add(new RolePermission { RoleId = 9999, PermissionId = 1 });
            Assert.Throws<DbUpdateException>(() => _db.SaveChanges());

            // Non-existing PermissionId
            _db.ChangeTracker.Clear();
            _db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 9999 });
            Assert.Throws<DbUpdateException>(() => _db.SaveChanges());
        }

        [Test]
        public void Delete_Role_With_Existing_Mappings_Is_Restricted()
        {
            if (_db == null) Assert.Ignore("MySQL not configured.");

            // WorkflowAdmin (Id=4) has seeded mappings ({2,4})
            var role = _db.Roles.Single(r => r.Id == 4);
            _db.Roles.Remove(role);

            // RESTRICT should block the delete since mappings exist
            Assert.Throws<DbUpdateException>(() => _db.SaveChanges());

            // After removing mappings, delete should succeed
            _db.ChangeTracker.Clear();
            var mappings = _db.RolePermissions.Where(rp => rp.RoleId == 4).ToList();
            _db.RolePermissions.RemoveRange(mappings);
            _db.SaveChanges();

            var roleAgain = _db.Roles.Single(r => r.Id == 4);
            _db.Roles.Remove(roleAgain);
            Assert.DoesNotThrow(() => _db.SaveChanges());
        }

        [Test]
        public void Delete_Permission_That_Is_Mapped_Is_Restricted()
        {
            if (_db == null) Assert.Ignore("MySQL not configured.");

            // Permission Id=4 ("ViewRespondVerifications") is mapped in seed
            var perm = _db.Permissions.Single(p => p.Id == 4);
            _db.Permissions.Remove(perm);
            Assert.Throws<DbUpdateException>(() => _db.SaveChanges());

            // Remove mappings then delete should pass
            _db.ChangeTracker.Clear();
            var mappings = _db.RolePermissions.Where(rp => rp.PermissionId == 4).ToList();
            _db.RolePermissions.RemoveRange(mappings);
            _db.SaveChanges();

            var permAgain = _db.Permissions.Single(p => p.Id == 4);
            _db.Permissions.Remove(permAgain);
            Assert.DoesNotThrow(() => _db.SaveChanges());
        }

        // ------------------- helpers -------------------

        private static string? TryReadDefaultConnectionFromIdvBackend()
        {
            // Locate the IDV_Backend project folder (walk up a few levels from the test bin)
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var backendDir = Path.Combine(dir.FullName, "IDV_Backend");
                var devSettings = Path.Combine(backendDir, "appsettings.Development.json");
                var baseSettings = Path.Combine(backendDir, "appsettings.json");
                if (Directory.Exists(backendDir) && (File.Exists(devSettings) || File.Exists(baseSettings)))
                {
                    var builder = new ConfigurationBuilder()
                        .SetBasePath(backendDir);

                    if (File.Exists(baseSettings))
                        builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                    if (File.Exists(devSettings))
                        builder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);

                    var cfg = builder.Build();
                    var cs = cfg.GetConnectionString("DefaultConnection");
                    return string.IsNullOrWhiteSpace(cs) ? null : cs;
                }
            }
            return null;
        }

        private static string StripDatabase(string conn)
        {
            // Remove Database/Initial Catalog if present
            var withoutDb = Regex.Replace(conn, @"(?i)(Database|Initial Catalog)\s*=\s*[^;]*;?", "");
            withoutDb = Regex.Replace(withoutDb, @";{2,}", ";").Trim().TrimEnd(';');
            return withoutDb;
        }

        private static string AddOrReplaceDatabase(string conn, string dbName)
        {
            conn = StripDatabase(conn);
            if (!conn.EndsWith(";")) conn += ";";
            return conn + $"Database={dbName};";
        }
    }
}
