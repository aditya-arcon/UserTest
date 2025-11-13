using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using IDV_Backend.Models;
using IDV_Backend.Repositories.Audit;
using IDV_Backend.Services.Audit;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace UserTest.Services.Audit
{
    [TestFixture]
    public class TemplateAuditLoggerTests
    {
        private Mock<IAuditRepository> _repo = null!;
        private Mock<IValidator<TemplateAuditLog>> _validator = null!;
        private Mock<ILogger<TemplateAuditLogger>> _logger = null!;
        private TemplateAuditLogger _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _repo = new Mock<IAuditRepository>(MockBehavior.Strict);
            _validator = new Mock<IValidator<TemplateAuditLog>>(MockBehavior.Strict);
            _logger = new Mock<ILogger<TemplateAuditLogger>>();
            _sut = new TemplateAuditLogger(_repo.Object, _logger.Object, _validator.Object);
        }

        [Test]
        public async Task LogAsync_ValidInput_PersistsLog()
        {
            // Arrange
            _validator.Setup(v => v.ValidateAsync(It.IsAny<TemplateAuditLog>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ValidationResult());

            _repo.Setup(r => r.AddTemplateAuditAsync(
                It.Is<TemplateAuditLog>(l =>
                    l.TemplateId == 10 &&
                    l.UserId == 20 &&
                    l.UserDisplayName == "Ada Lovelace" &&
                    l.Action == "TemplateUpdated" &&
                    l.Details == "Title changed"),
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            await _sut.LogAsync(10, 20, "Ada Lovelace", "TemplateUpdated", "Title changed");

            // Assert
            _repo.VerifyAll();
            _validator.VerifyAll();
        }

        [Test]
        public async Task LogAsync_InvalidTemplateId_SkipsNoThrow()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<TemplateAuditLog>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ValidationResult()); // should not be called, but safe

            await _sut.LogAsync(0, 20, "x", "Action", "d");
            _repo.Verify(r => r.AddTemplateAuditAsync(It.IsAny<TemplateAuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task LogAsync_ValidatorFails_SkipsPersistence()
        {
            // Arrange
            var vr = new ValidationResult(new[] { new ValidationFailure("Action", "bad") });
            _validator.Setup(v => v.ValidateAsync(It.IsAny<TemplateAuditLog>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(vr);

            // Act
            await _sut.LogAsync(10, 20, "x", "??", "d");

            // Assert
            _repo.Verify(r => r.AddTemplateAuditAsync(It.IsAny<TemplateAuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
            _validator.VerifyAll();
        }

        [Test]
        public void LogAsync_Cancelled_NoThrow()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _validator.Setup(v => v.ValidateAsync(It.IsAny<TemplateAuditLog>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ValidationResult());

            _repo.Setup(r => r.AddTemplateAuditAsync(It.IsAny<TemplateAuditLog>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new TaskCanceledException());

            // Act + Assert: should swallow cancellation
            Assert.DoesNotThrowAsync(() => _sut.LogAsync(1, 1, "u", "A", null, cts.Token));
        }
    }
}
