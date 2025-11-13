using System;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Models.Audit;
using IDV_Backend.Repositories.Audit;
using IDV_Backend.Services.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace UserTest.Services.Audit
{
    public class DummyEntity { public string Name { get; set; } = "X"; public int? N { get; set; } = 5; }

    [TestFixture]
    public class AuditLoggingServiceTests
    {
        private Mock<IAuditRepository> _repo = null!;
        private Mock<ILogger<AuditLoggingService>> _logger = null!;
        private AuditLoggingService _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _repo = new Mock<IAuditRepository>(MockBehavior.Strict);
            _logger = new Mock<ILogger<AuditLoggingService>>();
            _sut = new AuditLoggingService(_repo.Object, _logger.Object);
        }

        [Test]
        public async Task LogCreateAsync_WritesAuditLog()
        {
            _repo.Setup(r => r.AddAuditLogAsync(
                It.Is<AuditLog>(a =>
                    a.EntityName == nameof(DummyEntity) &&
                    a.EntityId == 42 &&
                    a.Action == "Created" &&
                    a.UserId == 7 &&
                    a.NewValues!.Contains("\"name\":\"X\"")),
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            await _sut.LogCreateAsync(42, new DummyEntity(), 7);

            _repo.VerifyAll();
        }

        [Test]
        public async Task LogUpdateAsync_WritesOldAndNew()
        {
            var oldE = new DummyEntity { Name = "A", N = 1 };
            var newE = new DummyEntity { Name = "B", N = null };

            _repo.Setup(r => r.AddAuditLogAsync(
                It.Is<AuditLog>(a =>
                    a.Action == "Updated" &&
                    a.OldValues!.Contains("\"name\":\"A\"") &&
                    a.NewValues!.Contains("\"name\":\"B\"") &&
                    a.NewValues!.Contains("\"n\":null")),
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            await _sut.LogUpdateAsync(9, oldE, newE, 1);

            _repo.VerifyAll();
        }

        [Test]
        public async Task LogDeleteAsync_WritesOldOnly()
        {
            var e = new DummyEntity { Name = "gone" };
            _repo.Setup(r => r.AddAuditLogAsync(
                It.Is<AuditLog>(a => a.Action == "Deleted" && a.OldValues!.Contains("gone") && a.NewValues == null),
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            await _sut.LogDeleteAsync(5, e, 2);

            _repo.VerifyAll();
        }

        [Test]
        public async Task LogCustomAction_UsesHttpContextHeaders()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
            ctx.Request.Headers["User-Agent"] = "UA";

            _repo.Setup(r => r.AddAuditLogAsync(
                It.Is<AuditLog>(a => a.Action == "Reset" &&
                                     a.IpAddress == "1.2.3.4" &&
                                     a.UserAgent!.Contains("UA")),
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            await _sut.LogCustomActionAsync("Thing", 1, "Reset", 99, "U", "d", new { a = 1 }, new { b = 2 }, ctx);

            _repo.VerifyAll();
        }

        [Test]
        public async Task GuardClauses_InvalidInputs_SkipPersistence()
        {
            await _sut.LogCustomActionAsync("", 1, "A", 1);
            await _sut.LogCustomActionAsync("E", 0, "A", 1);
            await _sut.LogCustomActionAsync("E", 1, "", 1);
            await _sut.LogCustomActionAsync("E", 1, "A", 0);

            _repo.Verify(r => r.AddAuditLogAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ReadMethods_PassThroughToRepository()
        {
            _repo.Setup(r => r.GetEntityAuditTrailAsync("X", 2, 1, 10, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Array.Empty<AuditLog>(), 0));
            _repo.Setup(r => r.GetUserAuditTrailAsync(9, 1, 10, null, null, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Array.Empty<AuditLog>(), 0));
            _repo.Setup(r => r.SearchAuditLogsAsync(null, null, null, null, null, null, 1, 50, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Array.Empty<AuditLog>(), 0));

            await _sut.GetEntityAuditTrailAsync("X", 2, 1, 10);
            await _sut.GetUserAuditTrailAsync(9, 1, 10);
            await _sut.SearchAuditLogsAsync();

            _repo.VerifyAll();
        }
    }
}
