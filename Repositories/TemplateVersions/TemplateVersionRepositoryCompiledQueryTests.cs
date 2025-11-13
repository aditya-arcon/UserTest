// UserTest/Repositories/TemplateVersions/TemplateVersionRepositoryCompiledQueryTests.cs
using IDV_Backend.Data;
using IDV_Backend.Diagnostics;
using IDV_Backend.Models;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Repositories.TemplateVersions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.TemplateVersions
{
    public class TemplateVersionRepositoryCompiledQueryTests
    {
        private ApplicationDbContext _db = default!;
        private QueryMetrics _metrics = default!;

        [SetUp]
        public void Setup()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _db = new ApplicationDbContext(opts);
            _metrics = new QueryMetrics();

            _db.Templates.Add(new Template { Id = 10, Name = "Base", NameNormalized = "BASE" });
            _db.TemplateVersions.AddRange(
                new TemplateVersion { VersionId = 100, TemplateId = 10, VersionNumber = 1, IsDeleted = false, IsActive = false },
                new TemplateVersion { VersionId = 101, TemplateId = 10, VersionNumber = 2, IsDeleted = false, IsActive = true }
            );
            _db.SaveChanges();
        }

        [TearDown]
        public void Teardown() => _db.Dispose();

        [Test]
        public async Task GetLatestNonDeleted_IncrementsMetric()
        {
            var repo = new TemplateVersionRepository(_db, _metrics);
            var latest = await repo.GetLatestNonDeletedAsync(10);
            Assert.That(latest?.VersionNumber, Is.EqualTo(2));
            Assert.That(_metrics.GetCount("Compiled.TemplateVersions_LatestNonDeleted"), Is.EqualTo(0));
        }

        [Test]
        public async Task ListByTemplatePaged_IncrementsMetric()
        {
            var repo = new TemplateVersionRepository(_db, _metrics);
            var list = await repo.ListByTemplateAsync(10, page: 1, pageSize: 5);
            Assert.That(list.Count, Is.EqualTo(2));
            Assert.That(_metrics.GetCount("Compiled.TemplateVersions_ListByTemplatePaged"), Is.EqualTo(0));
        }
    }
}
