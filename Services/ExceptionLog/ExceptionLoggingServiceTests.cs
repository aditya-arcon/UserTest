using IDV_Backend.Models.ExceptionLogs;
using IDV_Backend.Repositories.ExceptionLogs;
using IDV_Backend.Services.Common;
using IDV_Backend.Services.ExceptionLogging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;          // for IsDevelopment() extension (no setup!)
using Microsoft.Extensions.Logging;          // for ILogger<>
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;                      // for CancellationToken
using System.Threading.Tasks;
using Xunit;

// Avoid naming this namespace "ExceptionLog" to prevent collisions with the model type
namespace UserTest.Services.ExceptionLogging
{
    // Disambiguate the model type once and reuse
    using ExcLog = IDV_Backend.Models.ExceptionLogs.ExceptionLog;

    public sealed class ExceptionLoggingServiceTests
    {
        private readonly Mock<IExceptionLogRepository> _repo = new();
        private readonly Mock<ICurrentUser> _currentUser = new();
        private readonly Mock<IWebHostEnvironment> _env = new();
        private readonly Mock<ILogger<ExceptionLoggingService>> _logger = new();

        private ExceptionLoggingService CreateSut(bool isDev = true)
        {
            _currentUser.SetupGet(x => x.UserId).Returns(123);
            _currentUser.SetupGet(x => x.UserName).Returns("Jane Doe");

            // Set EnvironmentName; IsDevelopment() reads this. Don't try to Setup the extension method.
            _env.SetupGet(x => x.EnvironmentName).Returns(isDev ? "Development" : "Production");

            return new ExceptionLoggingService(_repo.Object, _currentUser.Object, _env.Object, _logger.Object);
        }

        private static HttpContext MakeHttpContext()
        {
            var ctx = new DefaultHttpContext();
            ctx.TraceIdentifier = "trace-123";
            ctx.Request.Method = "GET";
            ctx.Request.Path = "/api/thing";
            ctx.Request.Headers["User-Agent"] = "UnitTest UA";
            ctx.Request.Headers["Authorization"] = "Bearer secret";         // should be sanitized
            ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
            ctx.Request.QueryString = new QueryString("?token=abc&safe=ok"); // token should be sanitized
            return ctx;
        }

        [Fact]
        public async Task LogExceptionAsync_CreatesNewEntry_WhenNoDuplicate()
        {
            // arrange
            var sut = CreateSut();
            _repo.Setup(r => r.FindDuplicateAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((ExcLog?)null);  // nullable model

            ExcLog? persisted = null;
            _repo.Setup(r => r.AddAsync(It.IsAny<ExcLog>(), It.IsAny<CancellationToken>()))
                 .Callback<ExcLog, CancellationToken>((e, _) => persisted = e)
                 .Returns(Task.CompletedTask);

            // act
            var ex = new InvalidOperationException("Boom!");
            var id = await sut.LogExceptionAsync(ex, MakeHttpContext(), new Dictionary<string, object> { ["hint"] = 7 });

            // assert
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.NotNull(persisted);
            Assert.Equal("InvalidOperationException", persisted!.ExceptionType);
            Assert.Equal(ExceptionCategory.BusinessLogic, persisted.ExceptionCategory);
            Assert.Equal(SeverityLevel.Error, persisted.SeverityLevel);
            Assert.Equal("Boom!", persisted.Message);
            Assert.Equal("Development", persisted.EnvironmentName);
            Assert.Equal("/api/thing", persisted.RequestPath);
            Assert.Equal("GET", persisted.RequestMethod);
            Assert.Equal("trace-123", persisted.TraceId);
            Assert.Equal(123, persisted.UserId);
            Assert.Equal("Jane Doe", persisted.UserName);
            Assert.Contains("***", persisted.RequestHeaders); // sanitized Authorization hidden
            Assert.Contains("\"token\":\"***\"", persisted.QueryParameters); // sanitized token param hidden

            _repo.Verify(r => r.AddAsync(It.IsAny<ExcLog>(), It.IsAny<CancellationToken>()), Times.Once);
            _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task LogExceptionAsync_DetectsDuplicate_IncrementsOccurrenceCount()
        {
            // arrange
            var sut = CreateSut();
            var existing = new ExcLog
            {
                ExceptionId = Guid.NewGuid().ToString(),
                ExceptionType = "InvalidOperationException",
                Message = "Boom!",
                OccurrenceCount = 2,
                LastOccurredAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                EnvironmentName = "Development",
            };

            _repo.Setup(r => r.FindDuplicateAsync(
                    existing.ExceptionType, existing.Message, It.IsAny<string?>(), "Development",
                    It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(existing);

            // act
            var ex = new InvalidOperationException("Boom!");
            var id = await sut.LogExceptionAsync(ex, MakeHttpContext());

            // assert
            Assert.Equal(existing.ExceptionId, id);
            Assert.Equal(3, existing.OccurrenceCount);
            _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            _repo.Verify(r => r.AddAsync(It.IsAny<ExcLog>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task MarkAsResolvedAsync_UpdatesFields_AndSaves()
        {
            var sut = CreateSut();
            var log = new ExcLog
            {
                Id = 42,
                ExceptionId = Guid.NewGuid().ToString(),
                IsResolved = false
            };

            // Accept any bool for the flag (as the service may call true/false)
            _repo.Setup(r => r.GetByIdAsync(42, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(log);

            // SaveChangesAsync returns Task (not Task<int>)
            _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

            var ok = await sut.MarkAsResolvedAsync(42, resolvedBy: 999, notes: "fixed", CancellationToken.None);

            Assert.True(ok);
            Assert.True(log.IsResolved);
            Assert.Equal(999, log.ResolvedBy);
            Assert.NotNull(log.ResolvedAt);
            Assert.Equal("fixed", log.ResolutionNotes);
            _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }




        [Fact]
        public async Task GetTopExceptionTypesAsync_DelegatesToRepo_WithCorrectArgs()
        {
            var sut = CreateSut();
            _repo.Setup(r => r.GetTopTypesAsync(5, null, null, "Prod", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new List<ExceptionTypeCount> { new() { ExceptionType = "A", TotalOccurrences = 10 } });

            var res = await sut.GetTopExceptionTypesAsync(5, null, null, "Prod", CancellationToken.None);
            Assert.Single(res);
            Assert.Equal("A", res[0].ExceptionType);
        }

        [Fact]
        public async Task GetExceptionStatisticsAsync_ComputesCounters()
        {
            var sut = CreateSut();
            var logs = new List<ExcLog>
            {
                new() { SeverityLevel = SeverityLevel.Error,     IsResolved = true,  ExceptionCategory = ExceptionCategory.SystemError,  EnvironmentName = "Dev" },
                new() { SeverityLevel = SeverityLevel.Critical,  IsResolved = false, ExceptionCategory = ExceptionCategory.Validation,    EnvironmentName = "Dev" },
                new() { SeverityLevel = SeverityLevel.Warning,   IsResolved = true,  ExceptionCategory = ExceptionCategory.Validation,    EnvironmentName = "Prod" }
            };

            _repo.Setup(r => r.QueryAsync(
                    1, int.MaxValue, null, null, null,
                    It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                    null, "Dev", It.IsAny<CancellationToken>()))
                .ReturnsAsync((logs.Where(l => l.EnvironmentName == "Dev").ToList(), 2));

            var from = DateTime.UtcNow.AddDays(-1);
            var to = DateTime.UtcNow;

            var stats = await sut.GetExceptionStatisticsAsync(from, to, "Dev", CancellationToken.None);

            Assert.Equal(2, stats.TotalExceptions);
            Assert.Equal(1, stats.CriticalExceptions);
            Assert.Equal(1, stats.ErrorExceptions);
            Assert.Equal(0, stats.InfoExceptions);
            Assert.Equal(0, stats.WarningExceptions); // in "Dev" only two logs: Error + Critical
            Assert.Equal(1, stats.ResolvedExceptions);
            Assert.Equal(1, stats.UnresolvedExceptions);
            Assert.True(stats.ResolutionRate > 0m);
            Assert.True(stats.ExceptionsByCategory.Count > 0);
            Assert.True(stats.ExceptionsByEnvironment.Count > 0);
        }
    }
}
