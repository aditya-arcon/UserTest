// UserTest/Repositories/Templates/TemplateRepositoryCompiledQueryTests.cs
using IDV_Backend.Data;
using IDV_Backend.Diagnostics;
using IDV_Backend.Models;
using IDV_Backend.Models.User;
using IDV_Backend.Repositories.Templates;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.Templates
{
    public class TemplateRepositoryCompiledQueryTests
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

            _db.Users.Add(new User { Id = 1, Email = "a@b.com" });
            _db.Users.Add(new User { Id = 2, Email = "c@d.com" });

            _db.Templates.Add(new Template
            {
                Id = 101,
                Name = "KYC",
                NameNormalized = "KYC",
                CreatedBy = 1,
                UpdatedBy = 2,
                IsDeleted = false
            });
            _db.SaveChanges();
        }

        [TearDown]
        public void Teardown() => _db.Dispose();

        [Test]
        public async Task GetById_UsesCompiledQuery_IncrementsMetric()
        {
            var repo = new TemplateRepository(_db, _metrics);

            var t = await repo.GetByIdAsync(101, default);
            Assert.That(t, Is.Not.Null);
            Assert.That(_metrics.GetCount("Compiled.Templates_GetByIdNonDeleted"), Is.EqualTo(0));
        }

        [Test]
        public async Task GetAll_UsesCompiledQuery_IncrementsMetric()
        {
            var repo = new TemplateRepository(_db, _metrics);
            var list = await repo.GetAllAsync(default);
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(_metrics.GetCount("Compiled.Templates_GetAllNonDeleted"), Is.EqualTo(0));
        }
    }
}
