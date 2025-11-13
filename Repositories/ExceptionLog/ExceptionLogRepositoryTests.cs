using IDV_Backend.Data;
using IDV_Backend.Models.ExceptionLogs;
using IDV_Backend.Repositories.ExceptionLogs;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading; // CancellationToken
using System.Threading.Tasks;
using Xunit;

namespace UserTest.Repositories.ExceptionLogging // avoid "ExceptionLog" to prevent collisions
{
    using ExcLog = IDV_Backend.Models.ExceptionLogs.ExceptionLog;

    public sealed class ExceptionLogRepositoryTests : IDisposable
    {
        private readonly ApplicationDbContext _ctx;
        private readonly ExceptionLogRepository _repo;

        public ExceptionLogRepositoryTests()
        {
            // Unique DB name per test class instance to ensure isolation
            var dbName = $"ExceptionLogRepoTests_{Guid.NewGuid():N}";
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .EnableSensitiveDataLogging()
                .Options;

            _ctx = new ApplicationDbContext(options);
            _ctx.Database.EnsureCreated();

            _repo = new ExceptionLogRepository(_ctx);
        }

        [Fact]
        public async Task FindDuplicateAsync_Finds_ByTypeMessagePathEnv_AndTimeWindow()
        {
            // arrange
            var now = DateTimeOffset.UtcNow;
            var log = new ExcLog
            {
                ExceptionId = Guid.NewGuid().ToString(),
                ExceptionType = nameof(InvalidOperationException),
                Message = "Boom",
                RequestPath = "/api/test",
                EnvironmentName = "Development",
                LastOccurredAt = now.AddMinutes(-5),
                FirstOccurredAt = now.AddMinutes(-10),
                OccurredAt = now.AddMinutes(-10),
            };
            _ctx.ExceptionLogs.Add(log);
            await _ctx.SaveChangesAsync();

            // act
            var dup = await _repo.FindDuplicateAsync(
                nameof(InvalidOperationException), "Boom", "/api/test", "Development",
                now.AddMinutes(-60), CancellationToken.None);

            // assert
            Assert.NotNull(dup);
            Assert.Equal(log.Id, dup!.Id);
        }

        [Fact]
        public async Task QueryAsync_AppliesFilters_AndPaginates()
        {
            var now = DateTimeOffset.UtcNow;
            for (int i = 0; i < 30; i++)
            {
                _ctx.ExceptionLogs.Add(new ExcLog
                {
                    ExceptionId = Guid.NewGuid().ToString(),
                    ExceptionType = i % 2 == 0 ? "TypeA" : "TypeB",
                    ExceptionCategory = i % 3 == 0 ? ExceptionCategory.Validation : ExceptionCategory.SystemError,
                    SeverityLevel = i % 4 == 0 ? SeverityLevel.Critical : SeverityLevel.Error,
                    Message = "M" + i,
                    EnvironmentName = i % 2 == 0 ? "Production" : "Staging",
                    OccurredAt = now.AddMinutes(-i),
                    FirstOccurredAt = now.AddMinutes(-i - 1),
                    LastOccurredAt = now.AddMinutes(-i),
                    IsResolved = i % 5 == 0
                });
            }
            await _ctx.SaveChangesAsync();

            // Filter: Production + Error severity
            var (logs, total) = await _repo.QueryAsync(
                page: 1, pageSize: 10,
                severity: SeverityLevel.Error,
                category: null,
                userId: null,
                from: null, to: null,
                isResolved: null,
                environment: "Production",
                ct: CancellationToken.None);

            Assert.True(total > 0);
            Assert.True(logs.Count <= 10);
            Assert.All(logs, l =>
            {
                Assert.Equal("Production", l.EnvironmentName);
                Assert.Equal(SeverityLevel.Error, l.SeverityLevel);
            });
        }

        [Fact]
        public async Task GetTopTypesAsync_GroupsAndOrdersByTotalOccurrences()
        {
            var now = DateTimeOffset.UtcNow;
            _ctx.ExceptionLogs.AddRange(
                new ExcLog
                {
                    ExceptionId = Guid.NewGuid().ToString(),
                    ExceptionType = "A",
                    OccurrenceCount = 3,
                    EnvironmentName = "Prod",
                    OccurredAt = now,
                    FirstOccurredAt = now,
                    LastOccurredAt = now
                },
                new ExcLog
                {
                    ExceptionId = Guid.NewGuid().ToString(),
                    ExceptionType = "A",
                    OccurrenceCount = 2,
                    EnvironmentName = "Prod",
                    OccurredAt = now,
                    FirstOccurredAt = now,
                    LastOccurredAt = now
                },
                new ExcLog
                {
                    ExceptionId = Guid.NewGuid().ToString(),
                    ExceptionType = "B",
                    OccurrenceCount = 10,
                    EnvironmentName = "Prod",
                    OccurredAt = now,
                    FirstOccurredAt = now,
                    LastOccurredAt = now
                }
            );
            await _ctx.SaveChangesAsync();

            var top = await _repo.GetTopTypesAsync(2, null, null, "Prod", CancellationToken.None);
            Assert.Equal(2, top.Count);
            Assert.Equal("B", top.First().ExceptionType); // 10 first
            Assert.Equal(10, top.First().TotalOccurrences);
            Assert.Equal("A", top.Last().ExceptionType);  // 3+2=5
            Assert.Equal(5, top.Last().TotalOccurrences);
        }

        [Fact]
        public async Task SoftDeleteOlderThanAsync_SoftDeletes_AndKeepsCriticalWhenRequested()
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

            _ctx.ExceptionLogs.AddRange(
                new ExcLog
                {
                    ExceptionId = Guid.NewGuid().ToString(),
                    ExceptionType = "X",
                    SeverityLevel = SeverityLevel.Error,
                    EnvironmentName = "Prod",
                    OccurredAt = cutoff.AddDays(-1),
                    FirstOccurredAt = cutoff.AddDays(-2),
                    LastOccurredAt = cutoff.AddDays(-1)
                },
                new ExcLog
                {
                    ExceptionId = Guid.NewGuid().ToString(),
                    ExceptionType = "Y",
                    SeverityLevel = SeverityLevel.Critical,
                    EnvironmentName = "Prod",
                    OccurredAt = cutoff.AddDays(-1),
                    FirstOccurredAt = cutoff.AddDays(-2),
                    LastOccurredAt = cutoff.AddDays(-1)
                }
            );
            await _ctx.SaveChangesAsync();

            var count = await _repo.SoftDeleteOlderThanAsync(cutoff, keepCritical: true, CancellationToken.None);

            Assert.Equal(1, count);
            var all = await _ctx.ExceptionLogs.IgnoreQueryFilters().ToListAsync();
            Assert.True(all.First(l => l.ExceptionType == "X").IsDeleted);
            Assert.False(all.First(l => l.ExceptionType == "Y").IsDeleted); // kept because Critical
        }

        public void Dispose()
        {
            _ctx?.Dispose();
        }
    }
}
