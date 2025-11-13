using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Contracts.Audit;
using IDV_Backend.Models;
using IDV_Backend.Repositories.Audit;
using IDV_Backend.Controllers.Audit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace UserTest.Controllers.Audit
{
    [TestFixture]
    public class AuditControllerTests
    {
        private Mock<IAuditRepository> _repo = null!;
        private Mock<ILogger<AuditController>> _logger = null!;
        private AuditController _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _repo = new Mock<IAuditRepository>(MockBehavior.Strict);
            _logger = new Mock<ILogger<AuditController>>();
            _sut = new AuditController(_repo.Object, _logger.Object);
        }

        [Test]
        public async Task GetTemplateAuditLogs_HappyPath_OkWithPagedResponse()
        {
            // Arrange
            var templateId = 100L;
            _repo.Setup(r => r.TemplateExistsAsync(templateId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var rows = Enumerable.Range(1, 3).Select(i => new TemplateAuditLog
            {
                Id = i,
                TemplateId = templateId,
                UserId = i,
                UserDisplayName = "User " + i,
                Action = "Action" + i,
                Details = "D" + i,
                OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-i)
            }).ToList();

            _repo.Setup(r => r.GetTemplateAuditLogsAsync(templateId, 1, 50, null, null, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((rows, rows.Count));

            // Act
            var result = await _sut.GetTemplateAuditLogs(templateId);

            // Assert
            var ok = result as OkObjectResult;
            Assert.That(ok, Is.Not.Null);
            var payload = ok!.Value as PagedAuditLogsResponse;
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.TotalCount, Is.EqualTo(3));
            Assert.That(payload.Logs.Count, Is.EqualTo(3));
            Assert.That(payload.Logs.All(l => l.TemplateId == templateId), Is.True);

            _repo.VerifyAll();
        }

        [Test]
        public async Task GetTemplateAuditLogs_NotFoundTemplate_Returns404()
        {
            _repo.Setup(r => r.TemplateExistsAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var result = await _sut.GetTemplateAuditLogs(999);
            Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());

            _repo.VerifyAll();
        }

        [Test]
        public async Task GetUserAuditLogs_HappyPath()
        {
            var userId = 7L;
            _repo.Setup(r => r.UserExistsAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var rows = new[]
            {
                new TemplateAuditLog { Id = 1, TemplateId = 10, UserId = userId, Action = "A", OccurredAt = DateTimeOffset.UtcNow }
            }.ToList();

            _repo.Setup(r => r.GetUserTemplateAuditLogsAsync(userId, 1, 50, null, null, null, null, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((rows, 1));

            var result = await _sut.GetUserAuditLogs(userId);
            var ok = result as OkObjectResult;
            Assert.That(ok, Is.Not.Null);
            var payload = ok!.Value as PagedAuditLogsResponse;
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.Logs.Count, Is.EqualTo(1));

            _repo.VerifyAll();
        }

        [Test]
        public async Task GetUserAuditLogs_UserMissing_Returns404()
        {
            _repo.Setup(r => r.UserExistsAsync(111, It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var result = await _sut.GetUserAuditLogs(111);
            Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());

            _repo.VerifyAll();
        }

        [Test]
        public async Task GetAllAuditLogs_DefaultsDateRange_WhenMissing()
        {
            // Arrange: we assert that the call goes through; exact default window is internal.
            _repo.Setup(r => r.SearchTemplateAuditLogsAsync(
                    1, 50,
                    It.IsAny<DateTimeOffset?>(),
                    It.IsAny<DateTimeOffset?>(),
                    null, null, null,
                    It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Array.Empty<TemplateAuditLog>(), 0));

            // Act
            var result = await _sut.GetAllAuditLogs();

            // Assert
            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            _repo.VerifyAll();
        }

        [Test]
        public async Task GetAuditStatistics_HappyPath()
        {
            _repo.Setup(r => r.CountTemplateAuditLogsAsync(null, null, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(12);

            _repo.Setup(r => r.GetTopActionCountsAsync(null, null, 50, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new[] { ("TemplateUpdated", 7), ("TemplateCreated", 5) });

            _repo.Setup(r => r.GetTopActiveUsersAsync(null, null, 10, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new[] { (1L, "Alice", 4), (2L, (string?)null, 3) });

            _repo.Setup(r => r.GetTopModifiedTemplatesAsync(null, null, 10, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new[] { (100L, 6), (200L, 3) });

            var result = await _sut.GetAuditStatistics();

            var ok = result as OkObjectResult;
            Assert.That(ok, Is.Not.Null);
            var dto = ok!.Value as AuditStatisticsDto;
            Assert.That(dto, Is.Not.Null);
            Assert.That(dto!.TotalLogs, Is.EqualTo(12));
            Assert.That(dto.ActionCounts.Count, Is.EqualTo(2));
            Assert.That(dto.TopActiveUsers.First(u => u.UserId == 2).UserDisplayName, Is.EqualTo("Unknown"));

            _repo.VerifyAll();
        }

        [Test]
        public async Task GetTemplateAuditLogs_BadPaging_Returns400()
        {
            var result = await _sut.GetTemplateAuditLogs(templateId: 1, page: 0);
            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task GetAllAuditLogs_DateRangeTooLarge_Returns400()
        {
            var from = DateTimeOffset.UtcNow.AddDays(-400);
            var to = DateTimeOffset.UtcNow;
            var result = await _sut.GetAllAuditLogs(fromDate: from, toDate: to);
            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }
    }
}
