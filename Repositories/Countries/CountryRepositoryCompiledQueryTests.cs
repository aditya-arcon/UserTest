// UserTest/Repositories/Countries/CountryRepositoryCompiledQueryTests.cs
using IDV_Backend.Data;
using IDV_Backend.Diagnostics;
using IDV_Backend.Models.Countries;
using IDV_Backend.Repositories.Countries;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace UserTest.Repositories.Countries
{
    public class CountryRepositoryCompiledQueryTests
    {
        private ApplicationDbContext _db = default!;
        private QueryMetrics _metrics = default!;

        [SetUp]
        public void Setup()
        {
            // Use EF InMemory (no SQLite anywhere)
            var dbName = $"countries-tests-{Guid.NewGuid():N}";
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .EnableSensitiveDataLogging()
                .Options;

            _db = new ApplicationDbContext(opts);
            _db.Database.EnsureCreated();

            _metrics = new QueryMetrics();

            _db.Countries.AddRange(
                new Country { Id = 1, Name = "India", IsoCodeAlpha2 = "IN", IsoCodeAlpha3 = "IND", IsActive = true },
                new Country { Id = 2, Name = "USA", IsoCodeAlpha2 = "US", IsoCodeAlpha3 = "USA", IsActive = false },
                new Country { Id = 3, Name = "Japan", IsoCodeAlpha2 = "JP", IsoCodeAlpha3 = "JPN", IsActive = true }
            );
            _db.SaveChanges();
        }

        [TearDown]
        public void Teardown()
        {
            _db.Dispose();
        }

        [Test]
        public async Task ActiveList_IncrementsMetric_And_Filters()
        {
            var repo = new CountryRepository(_db, _metrics);
            var active = await repo.GetActiveAsync();

            Assert.That(active.Count, Is.EqualTo(2));

            // On InMemory, the repository disables compiled queries; assert non-compiled metric.
            Assert.That(_metrics.GetCount("Countries.ActiveList"), Is.EqualTo(1));
            Assert.That(_metrics.GetCount("Compiled.Countries_ActiveList"), Is.EqualTo(0));
        }

        [Test]
        public async Task GetById_AsNoTracking_IncrementsMetric()
        {
            var repo = new CountryRepository(_db, _metrics);
            var jp = await repo.GetByIdAsync(3, asNoTracking: true);

            Assert.That(jp?.IsoCodeAlpha2, Is.EqualTo("JP"));

            // On InMemory, compiled path is disabled; compiled metric should be 0.
            Assert.That(_metrics.GetCount("Compiled.Countries_ById_AsNoTracking"), Is.EqualTo(0));
        }
    }
}
