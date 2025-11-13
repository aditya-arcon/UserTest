using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.Audit;
using IDV_Backend.Models.User;
using IDV_Backend.Repositories.Audit;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using UserTest.Infra;

namespace UserTest.Repositories.Audit
{
    [TestFixture]
    public class AuditRepositoryTests
    {
        private DbContextOptions<ApplicationDbContext> _options = null!;
        private string _dbName = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Use a UNIQUE in-memory store for this fixture to avoid cross-fixture pollution.
            _dbName = "AuditRepoTests_" + Guid.NewGuid().ToString("N");

            _options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(_dbName)
                .EnableSensitiveDataLogging()
                .Options;

            using var db = new ApplicationDbContext(_options);
            db.Database.EnsureCreated();
        }

        [TearDown]
        public void TearDown()
        {
            using var db = new ApplicationDbContext(_options);
            db.TemplateAuditLogs.RemoveRange(db.TemplateAuditLogs);
            db.AuditLogs.RemoveRange(db.AuditLogs);
            db.Templates.RemoveRange(db.Templates);
            db.Users.RemoveRange(db.Users);
            db.SaveChanges();
        }

        private static (List<TemplateAuditLog> tal, List<AuditLog> al) Seed(ApplicationDbContext db)
        {
            db.Templates.AddRange(
                new Template { Id = 100, Name = "Onboarding A", CreatedAt = DateTimeOffset.UtcNow },
                new Template { Id = 200, Name = "Onboarding B", CreatedAt = DateTimeOffset.UtcNow }
            );

            db.Users.AddRange(
                new User { Id = 1, Email = "alice@example.com", FirstName = "Alice", LastName = "A" },
                new User { Id = 2, Email = "bob@example.com", FirstName = "Bob", LastName = "B" }
            );

            var now = DateTimeOffset.UtcNow;

            var tal = new List<TemplateAuditLog>
            {
                new TemplateAuditLog { TemplateId = 100, UserId = 1, UserDisplayName = "Alice", Action = "TemplateCreated", Details = "init",    OccurredAt = now.AddDays(-3) },
                new TemplateAuditLog { TemplateId = 100, UserId = 1, UserDisplayName = "Alice", Action = "TemplateUpdated", Details = "title",   OccurredAt = now.AddDays(-2) },
                // Make display name consistent for user 2 to avoid split buckets in top-users stats.
                new TemplateAuditLog { TemplateId = 100, UserId = 2, UserDisplayName = "Bob",   Action = "TemplateUpdated", Details = "fields",  OccurredAt = now.AddDays(-1) },
                new TemplateAuditLog { TemplateId = 200, UserId = 2, UserDisplayName = "Bob",   Action = "TemplateDeleted", Details = "cleanup", OccurredAt = now.AddHours(-2) },
            };

            var al = new List<AuditLog>
            {
                new AuditLog { EntityName = "Country",    EntityId = 10, UserId = 1, Action = "Created", Details = "C1",       OccurredAt = now.AddDays(-2) },
                new AuditLog { EntityName = "Country",    EntityId = 10, UserId = 2, Action = "Updated", Details = "C1 name",  OccurredAt = now.AddDays(-1) },
                new AuditLog { EntityName = "Department", EntityId = 7,  UserId = 1, Action = "Deleted", Details = "D7",       OccurredAt = now.AddHours(-5) },
            };

            db.TemplateAuditLogs.AddRange(tal);
            db.AuditLogs.AddRange(al);
            db.SaveChanges();

            return (tal, al);
        }

        private static AuditRepository CreateRepo(ApplicationDbContext db) => new AuditRepository(db);

        // --------------------- Template / existence ---------------------

        [Test]
        public async Task TemplateExists_And_UserExists_Work()
        {
            using var db = new SqliteFriendlyApplicationDbContext(_options);
            Seed(db);
            var repo = CreateRepo(db);

            Assert.That(await repo.TemplateExistsAsync(100), Is.True);
            Assert.That(await repo.TemplateExistsAsync(999), Is.False);

            Assert.That(await repo.UserExistsAsync(1), Is.True);
            Assert.That(await repo.UserExistsAsync(999), Is.False);
        }

        // --------------------- Template logs: paging + filters ---------------------

        [Test]
        public async Task GetTemplateAuditLogs_PagingAndFilters()
        {
            using var db = new SqliteFriendlyApplicationDbContext(_options);
            Seed(db);
            var repo = CreateRepo(db);

            var from = DateTimeOffset.UtcNow.AddDays(-2.5);
            var to = DateTimeOffset.UtcNow;

            (IReadOnlyList<TemplateAuditLog> rows, int total) result1 = await repo.GetTemplateAuditLogsAsync(
                templateId: 100,
                page: 1,
                pageSize: 2,
                fromDate: from,
                toDate: to,
                action: "Updated",
                userId: null,
                CancellationToken.None);

            // There are TWO 'Updated' rows for template 100 in that window.
            Assert.That(result1.total, Is.EqualTo(2));
            Assert.That(result1.rows.Count, Is.EqualTo(2));
            Assert.That(result1.rows.All(r => r.Action == "TemplateUpdated" && r.TemplateId == 100), Is.True);

            (IReadOnlyList<TemplateAuditLog> rows2, int total2) = await repo.GetTemplateAuditLogsAsync(
                100, 2, 2, from, to, "Updated", null);
            Assert.That(total2, Is.EqualTo(2));
            Assert.That(rows2.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task GetUserTemplateAuditLogs_FilterByTemplate_And_Action()
        {
            using var db = new SqliteFriendlyApplicationDbContext(_options);
            Seed(db);
            var repo = CreateRepo(db);

            (IReadOnlyList<TemplateAuditLog> rows, int total) = await repo.GetUserTemplateAuditLogsAsync(
                userId: 1,
                page: 1,
                pageSize: 10,
                fromDate: null,
                toDate: null,
                action: "Updated",
                templateId: 100,
                CancellationToken.None);

            Assert.That(total, Is.EqualTo(1));
            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(rows[0].TemplateId, Is.EqualTo(100));
            Assert.That(rows[0].Action, Is.EqualTo("TemplateUpdated"));
        }

        [Test]
        public async Task SearchTemplateAuditLogs_AllFiltersAndPaging()
        {
            using var db = new SqliteFriendlyApplicationDbContext(_options);
            Seed(db);
            var repo = CreateRepo(db);

            var from = DateTimeOffset.UtcNow.AddDays(-4);
            var to = DateTimeOffset.UtcNow;

            (IReadOnlyList<TemplateAuditLog> rows, int total) = await repo.SearchTemplateAuditLogsAsync(
                page: 1,
                pageSize: 2,
                fromDate: from,
                toDate: to,
                action: "Template",
                userId: 1,
                templateId: 100,
                CancellationToken.None);

            Assert.That(total, Is.EqualTo(2), "User 1 on template 100 has two rows");
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows.All(r => r.UserId == 1 && r.TemplateId == 100), Is.True);
        }

        // --------------------- Template stats ---------------------

        [Test]
        public async Task Count_And_TopBreakdowns_Work()
        {
            using var db = new SqliteFriendlyApplicationDbContext(_options);
            Seed(db);
            var repo = CreateRepo(db);

            var total = await repo.CountTemplateAuditLogsAsync(null, null, CancellationToken.None);
            Assert.That(total, Is.EqualTo(4));

            var topActions = (await repo.GetTopActionCountsAsync(null, null, 10, CancellationToken.None)).ToList();
            Assert.That(topActions[0].Action, Is.EqualTo("TemplateUpdated"));
            Assert.That(topActions[0].Count, Is.EqualTo(2));
            Assert.That(topActions.Sum(a => a.Count), Is.EqualTo(4)); // 1 created + 2 updated + 1 deleted

            var topUsers = (await repo.GetTopActiveUsersAsync(null, null, 10, CancellationToken.None)).ToList();
            Assert.That(topUsers.Count, Is.EqualTo(2));
            Assert.That(topUsers.First(t => t.UserId == 1).Count, Is.EqualTo(2));
            Assert.That(topUsers.First(t => t.UserId == 2).Count, Is.EqualTo(2));

            var topTemplates = (await repo.GetTopModifiedTemplatesAsync(null, null, 10, CancellationToken.None)).ToList();
            Assert.That(topTemplates.Count, Is.EqualTo(2));
            Assert.That(topTemplates.First(t => t.TemplateId == 100).Count, Is.EqualTo(3));
            Assert.That(topTemplates.First(t => t.TemplateId == 200).Count, Is.EqualTo(1));
        }

        // --------------------- Generic audit: write + queries ---------------------

        [Test]
        public async Task AddAuditLog_Persists_And_CanBeQueried()
        {
            using var db = new SqliteFriendlyApplicationDbContext(_options);
            Seed(db);
            var repo = CreateRepo(db);

            var log = new AuditLog
            {
                EntityName = "Department",
                EntityId = 77,
                UserId = 1,
                Action = "Created",
                Details = "seed",
                OccurredAt = DateTimeOffset.UtcNow
            };

            await repo.AddAuditLogAsync(log, CancellationToken.None);

            (IReadOnlyList<AuditLog> list, int total) =
                await repo.GetEntityAuditTrailAsync("Department", 77, 1, 50, CancellationToken.None);

            Assert.That(total, Is.EqualTo(1));
            Assert.That(list[0].Action, Is.EqualTo("Created"));
        }

        [Test]
        public async Task GetUserAuditTrail_FiltersByDate()
        {
            using var db = new SqliteFriendlyApplicationDbContext(_options);
            Seed(db);
            var repo = CreateRepo(db);

            var from = DateTimeOffset.UtcNow.AddDays(-1.5);
            var to = DateTimeOffset.UtcNow;

            (IReadOnlyList<AuditLog> rows, int total) =
                await repo.GetUserAuditTrailAsync(
                    userId: 1,
                    page: 1,
                    pageSize: 50,
                    fromDate: from.UtcDateTime,
                    toDate: to.UtcDateTime,
                    CancellationToken.None);

            Assert.That(total, Is.EqualTo(1));
            Assert.That(rows[0].Action, Is.EqualTo("Deleted"));
        }

        [Test]
        public async Task SearchAuditLogs_AllFilters()
        {
            using var db = new SqliteFriendlyApplicationDbContext(_options);
            Seed(db);
            var repo = CreateRepo(db);

            var from = DateTimeOffset.UtcNow.AddDays(-3);
            var to = DateTimeOffset.UtcNow;

            (IReadOnlyList<AuditLog> rows, int total) = await repo.SearchAuditLogsAsync(
                entityName: "Country",
                entityId: 10,
                userId: 2,
                action: "Updated",
                fromDate: from.UtcDateTime,
                toDate: to.UtcDateTime,
                page: 1,
                pageSize: 10,
                CancellationToken.None);

            Assert.That(total, Is.EqualTo(1));
            Assert.That(rows[0].Action, Is.EqualTo("Updated"));
            Assert.That(rows[0].UserId, Is.EqualTo(2));
        }
    }
}
