// UserTest/Diagnostics/QueryBenchmarkRunnerTests.cs
using IDV_Backend.Data;
using IDV_Backend.Diagnostics;
using IDV_Backend.Models;
using IDV_Backend.Models.User;
using IDV_Backend.Models.Countries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace UserTest.Diagnostics
{
    public class QueryBenchmarkRunnerTests
    {
        private ApplicationDbContext _db = default!;
        private ILogger<QueryBenchmarkRunner> _logger = default!;

        [SetUp]
        public void Setup()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            _db = new ApplicationDbContext(opts);

            // Minimal logger stub
            using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
            _logger = loggerFactory.CreateLogger<QueryBenchmarkRunner>();

            // Seed: one template + some countries
            _db.Users.Add(new User { Id = 1, Email = "a@b.com" });
            _db.Users.Add(new User { Id = 2, Email = "c@d.com" });

            _db.Templates.Add(new Template
            {
                Id = 42,
                Name = "Bench",
                NameNormalized = "BENCH",
                CreatedBy = 1,
                UpdatedBy = 2,
                IsDeleted = false
            });

            for (int i = 0; i < 300; i++)
            {
                _db.Countries.Add(new Country
                {
                    Id = 1000 + i,
                    Name = $"Country {i:D3}",
                    IsoCodeAlpha2 = $"C{i % 26:X}",
                    IsoCodeAlpha3 = $"C{i % 26:X}X",
                    IsActive = i % 2 == 0
                });
            }

            _db.SaveChanges();
        }

        [TearDown]
        public void Teardown() => _db.Dispose();

        [Test]
        public void Benchmark_Template_ById_Runs_And_Returns_Timings()
        {
            var runner = new QueryBenchmarkRunner(_db, _logger);
            var result = runner.Benchmark_Template_ById(42, iterations: 50);

            Assert.That(result.Iterations, Is.EqualTo(50));
            Assert.That(result.CompiledElapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.LinqElapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(0));

            TestContext.Out.WriteLine($"Template_ById: compiled={result.CompiledElapsed.TotalMilliseconds:F2} ms, linq={result.LinqElapsed.TotalMilliseconds:F2} ms, speedup={result.Speedup:F3}");
        }

        [Test]
        public void Benchmark_Country_ActiveList_Runs_And_Returns_Timings()
        {
            var runner = new QueryBenchmarkRunner(_db, _logger);
            var result = runner.Benchmark_Country_ActiveList(iterations: 10);

            Assert.That(result.Iterations, Is.EqualTo(10));
            Assert.That(result.CompiledElapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.LinqElapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(0));

            TestContext.Out.WriteLine($"Country_ActiveList: compiled={result.CompiledElapsed.TotalMilliseconds:F2} ms, linq={result.LinqElapsed.TotalMilliseconds:F2} ms, speedup={result.Speedup:F3}");
        }
    }
}
