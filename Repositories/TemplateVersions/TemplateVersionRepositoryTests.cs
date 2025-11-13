using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IDV_Backend.Data;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Repositories.TemplateVersions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.TemplateVersions
{
    public sealed class TemplateVersionRepositoryTests
    {
        private static ApplicationDbContext NewDb(string name)
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name).EnableSensitiveDataLogging().Options;
            var db = new ApplicationDbContext(opts);
            db.Database.EnsureCreated();
            return db;
        }

        [Test]
        public async Task GetMaxVersionNumberAsync_Returns_Expected()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            var repo = new TemplateVersionRepository(db);

            db.TemplateVersions.AddRange(
                new TemplateVersion { TemplateId = 1, VersionNumber = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new TemplateVersion { TemplateId = 1, VersionNumber = 2, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();

            var max = await repo.GetMaxVersionNumberAsync(1);
            max.Should().Be(2);
        }

        [Test]
        public async Task VersionNameExistsAsync_Detects_Name_Excluding_Id()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            var repo = new TemplateVersionRepository(db);

            var v = new TemplateVersion
            {
                TemplateId = 10,
                VersionNumber = 1,
                VersionName = "V1",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.TemplateVersions.Add(v);
            await db.SaveChangesAsync();

            (await repo.VersionNameExistsAsync(10, "V1", null)).Should().BeTrue();
            (await repo.VersionNameExistsAsync(10, "V1", v.VersionId)).Should().BeFalse();
        }

        [Test]
        public async Task DeactivateOthersAsync_Sets_Inactive_And_Counts()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            var repo = new TemplateVersionRepository(db);

            var v1 = new TemplateVersion { TemplateId = 7, VersionNumber = 1, Status = TemplateVersionStatus.Active, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var v2 = new TemplateVersion { TemplateId = 7, VersionNumber = 2, Status = TemplateVersionStatus.Draft, IsActive = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.TemplateVersions.AddRange(v1, v2);
            await db.SaveChangesAsync();

            var changed = await repo.DeactivateOthersAsync(7, v2.VersionId, 1, DateTime.UtcNow);
            changed.Should().Be(1);

            var rows = await db.TemplateVersions.OrderBy(x => x.VersionNumber).ToListAsync();
            rows[0].IsActive.Should().BeFalse();
            rows[0].Status.Should().Be(TemplateVersionStatus.Inactive);
        }
    }
}
