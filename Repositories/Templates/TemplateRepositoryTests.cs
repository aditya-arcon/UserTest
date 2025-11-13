using FluentAssertions;
using IDV_Backend.Constants;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Repositories.Templates;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace UserTest.Repositories.Templates
{
    public sealed class TemplateRepositoryTests
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
        public async Task CreateTemplateGraphAsync_Creates_Template_Version_And_Default_Sections()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            var repo = new TemplateRepository(db);

            var t = new Template
            {
                Name = "Onboarding",
                NameNormalized = "ONBOARDING",
                CreatedBy = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                Mode = TemplateMode.Standard
            };
            var v = new TemplateVersion
            {
                VersionNumber = 1,
                VersionName = "Onboarding",
                Status = TemplateVersionStatus.Draft,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await repo.CreateTemplateGraphAsync(t, v, TemplateMode.Standard, 1, default);

            (await db.Templates.CountAsync()).Should().Be(1);
            (await db.TemplateVersions.CountAsync()).Should().Be(1);
            (await db.TemplateSections.CountAsync()).Should().BeGreaterThanOrEqualTo(2);    // personal + documents
            (await db.TemplateSections.AnyAsync(s => s.SectionType == SectionTypes.Biometrics && s.IsActive)).Should().BeFalse();
        }

        [Test]
        public async Task ExistsByNormalizedNameAsync_Respects_ExcludeId()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            var repo = new TemplateRepository(db);

            var t = new Template { Name = "A", NameNormalized = "A", CreatedBy = 1, CreatedAt = DateTimeOffset.UtcNow };
            db.Templates.Add(t); await db.SaveChangesAsync();

            (await repo.ExistsByNormalizedNameAsync("A", null, default)).Should().BeTrue();
            (await repo.ExistsByNormalizedNameAsync("A", t.Id, default)).Should().BeFalse();
        }

        [Test]
        public async Task HasOutstandingInvitesAsync_False_When_None()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            var repo = new TemplateRepository(db);

            var v = new TemplateVersion { TemplateId = 10, VersionNumber = 1, Status = TemplateVersionStatus.Active, CreatedBy = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.TemplateVersions.Add(v); await db.SaveChangesAsync();

            (await repo.HasOutstandingInvitesAsync(new[] { v.VersionId }, DateTime.UtcNow, default)).Should().BeFalse();
        }
    }
}
