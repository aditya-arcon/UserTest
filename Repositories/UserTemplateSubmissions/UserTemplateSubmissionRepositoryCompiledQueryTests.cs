// UserTest/Repositories/UserTemplateSubmissions/UserTemplateSubmissionRepositoryCompiledQueryTests.cs
using IDV_Backend.Data;
using IDV_Backend.Diagnostics;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Models.User;
using IDV_Backend.Models.UserTemplateSubmissions;
using IDV_Backend.Repositories.UserTemplateSubmissions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.UserTemplateSubmissions
{
    public class UserTemplateSubmissionRepositoryCompiledQueryTests
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

            _db.Users.Add(new User { Id = 9, Email = "u@ex.com" });
            _db.TemplateVersions.Add(new TemplateVersion
            {
                VersionId = 77,
                TemplateId = 1,
                VersionNumber = 1,
                IsDeleted = false
            });
            _db.UserTemplateSubmissions.Add(new UserTemplateSubmission
            {
                Id = 555,
                TemplateVersionId = 77,
                UserId = 9,
                CreatedAtUtc = DateTime.UtcNow
            });
            _db.SaveChanges();
        }

        [TearDown]
        public void Teardown() => _db.Dispose();

        [Test]
        public async Task FindById_IncrementsMetric()
        {
            var repo = new UserTemplateSubmissionRepository(_db, _metrics);
            var s = await repo.FindByIdAsync(555);
            Assert.That(s, Is.Not.Null);
            Assert.That(_metrics.GetCount("Compiled.Submissions_ById"), Is.EqualTo(1));
        }

        [Test]
        public async Task FindByUserAndVersion_RespectFilter_IncrementsCorrectMetric()
        {
            var repo = new UserTemplateSubmissionRepository(_db, _metrics);
            var s = await repo.FindByUserAndTemplateVersionAsync(9, 77, includeSoftDeleted: false);
            Assert.That(s, Is.Not.Null);
            Assert.That(_metrics.GetCount("Compiled.Submissions_ByUserAndVersion_RespectFilter"), Is.EqualTo(1));
            Assert.That(_metrics.GetCount("Compiled.Submissions_ByUserAndVersion_IgnoreFilter"), Is.EqualTo(0));
        }
    }
}
