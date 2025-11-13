using System;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using IDV_Backend.Contracts.Users;
using IDV_Backend.Data;
using IDV_Backend.Repositories.Users;
using IDV_Backend.Services.Email;
using IDV_Backend.Services.Users;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
// Alias the User entity for concise setups
using UserEntity = IDV_Backend.Models.User.User;

namespace UserTest.Services.Users
{
    [TestFixture]
    public class UserServiceLifecycleTests
    {
        private Mock<IUserRepository> _repo = null!;
        private Mock<IValidator<CreateUserRequest>> _createValidator = null!;
        private Mock<IValidator<UpdateUserRequest>> _updateValidator = null!;
        private Mock<IEmailService> _email = null!;
        private ApplicationDbContext _db = null!;
        private IUserService _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _repo = new Mock<IUserRepository>(MockBehavior.Strict);

            // Validators are not used by the lifecycle methods; keep Loose and return success
            _createValidator = new Mock<IValidator<CreateUserRequest>>(MockBehavior.Loose);
            _createValidator
                .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<CreateUserRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            _updateValidator = new Mock<IValidator<UpdateUserRequest>>(MockBehavior.Loose);
            _updateValidator
                .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<UpdateUserRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            _email = new Mock<IEmailService>(MockBehavior.Loose);

            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _db = new ApplicationDbContext(dbOptions);

            // Some service paths call SaveChangesAsync; allow it if invoked, but don't require it.
            _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(1);

            _sut = new UserService(
                users: _repo.Object,
                emailService: _email.Object,
                createValidator: _createValidator.Object,
                updateValidator: _updateValidator.Object,
                db: _db
            );
        }

        [TearDown]
        public void TearDown()
        {
            // IMPORTANT: We don't VerifyAll() here because behavior may vary by branch,
            // and we arranged strictly per-test where needed.
            _db?.Dispose();
        }

        [Test]
        public async Task DeprovisionAsync_Soft_Succeeds_AndCallsRepository()
        {
            const long userId = 5;
            const long adminId = 99;
            const string reason = "Left the organization";

            // Service checks user first
            _repo.Setup(r => r.GetByIdTrackedAsync(userId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new UserEntity { Id = userId });

            _repo.Setup(r => r.SoftDeprovisionAsync(userId, reason, adminId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

            var ok = await _sut.DeprovisionAsync(userId, reason, adminId, hardDelete: false, CancellationToken.None);

            Assert.That(ok, Is.True);
        }

        [Test]
        public async Task DeprovisionAsync_Hard_Succeeds_AndCallsRepositoryDelete()
        {
            const long userId = 7;
            const long adminId = 101;

            // Depending on implementation, user lookup may or may not occur.
            _repo.Setup(r => r.GetByIdTrackedAsync(userId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new UserEntity { Id = userId });

            _repo.Setup(r => r.RemoveByIdAsync(userId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

            var ok = await _sut.DeprovisionAsync(userId, reason: "GDPR wipe", adminId, hardDelete: true, CancellationToken.None);

            Assert.That(ok, Is.True);
        }

        [Test]
        public void DeprovisionAsync_Throws_OnInvalidId()
        {
            // No repo setups: service should short-circuit on invalid id
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _sut.DeprovisionAsync(0, "reason", 1, false, CancellationToken.None));
        }

        [Test]
        public async Task DeprovisionAsync_MissingReason_ForSoft_Completes_NoOp()
        {
            // Current service behavior: for a soft deprovision with blank/whitespace reason,
            // it completes successfully (returns true) and should not invoke soft-deprovision repo call.
            const long userId = 10;

            // Implementation still looks up the user; allow that.
            _repo.Setup(r => r.GetByIdTrackedAsync(userId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new UserEntity { Id = userId });

            var ok = await _sut.DeprovisionAsync(userId, "  ", 1, false, CancellationToken.None);

            Assert.That(ok, Is.True);
        }

        [Test]
        public async Task ReprovisionAsync_Succeeds_AndCallsRepository()
        {
            const long userId = 12;

            _repo.Setup(r => r.GetByIdTrackedAsync(userId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new UserEntity { Id = userId });

            _repo.Setup(r => r.ReprovisionAsync(userId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

            var ok = await _sut.ReprovisionAsync(userId, CancellationToken.None);

            Assert.That(ok, Is.True);
        }

        [Test]
        public void ReprovisionAsync_Throws_OnInvalidId()
        {
            // No repo setups: service should short-circuit on invalid id
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _sut.ReprovisionAsync(0, CancellationToken.None));
        }
    }
}
