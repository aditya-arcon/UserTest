using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using IDV_Backend.Contracts.Users;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.User;
using IDV_Backend.Repositories.Users;
using IDV_Backend.Services.Email;
using IDV_Backend.Services.Users;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace UserTest.Services.Users
{
    [TestFixture]
    public class UserServiceTests
    {
        private Mock<IUserRepository> _repo = null!;
        private Mock<IValidator<CreateUserRequest>> _createValidator = null!;
        private Mock<IValidator<UpdateUserRequest>> _updateValidator = null!;
        private Mock<IEmailService> _emailService = null!;
        private Mock<ApplicationDbContext> _db = null!;
        private UserService _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _repo = new Mock<IUserRepository>(MockBehavior.Strict);

            // Validators are not always invoked in every test path; keep them Loose
            _createValidator = new Mock<IValidator<CreateUserRequest>>(MockBehavior.Loose);
            _createValidator
                .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<CreateUserRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _createValidator
                .Setup(v => v.Validate(It.IsAny<ValidationContext<CreateUserRequest>>()))
                .Returns(new ValidationResult());

            _updateValidator = new Mock<IValidator<UpdateUserRequest>>(MockBehavior.Loose);
            _updateValidator
                .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<UpdateUserRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _updateValidator
                .Setup(v => v.Validate(It.IsAny<ValidationContext<UpdateUserRequest>>() ))
                .Returns(new ValidationResult());

            _emailService = new Mock<IEmailService>(MockBehavior.Loose);

            // Provide DbContextOptions to allow Moq to construct an ApplicationDbContext proxy.
            // Using an empty options instance is sufficient here because tests mock repository interactions,
            // and we don't rely on a real database in these unit tests.
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().Options;
            _db = new Mock<ApplicationDbContext>(MockBehavior.Loose, options);

            _sut = new UserService(
                _repo.Object,
                _emailService.Object,
                _createValidator.Object,
                _updateValidator.Object,
                _db.Object
            );
        }

        [TearDown]
        public void TearDown()
        {
            // Only the repository must be called exactly as arranged
            _repo.VerifyAll();

            // Do not VerifyAll on validators; some tests intentionally short-circuit before validation.
            // Add targeted Verify(...) inside individual tests if you want to assert validator calls.
        }

        [Test]
        public void CreateAsync_Throws_WhenEmailMissing()
        {
            var req = new CreateUserRequest(
                FirstName: "A",
                LastName: "B",
                Email: "   ",  // whitespace to trigger
                Phone: null,
                RoleId: 1,
                Password: "p",
                ClientReferenceId: null,
                DeptId: null
            );

            Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(req));
        }

        [Test]
        public void GetByIdAsync_Throws_WhenIdInvalid()
        {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _sut.GetByIdAsync(0));
        }

        [Test]
        public async Task CreateAsync_Succeeds_WhenNewEmail()
        {
            var req = new CreateUserRequest(
                FirstName: "Ada",
                LastName: "Lovelace",
                Email: "Ada@Example.Com",
                Phone: null,
                RoleId: 1,
                Password: "p@ss",
                ClientReferenceId: null,
                DeptId: null
            );

            _repo.Setup(r => r.EmailExistsWithRoleAsync("ada@example.com", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

            _repo.Setup(r => r.AddUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

            _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(1);

            _repo.Setup(r => r.GetRoleNameByIdAsync(1, It.IsAny<CancellationToken>()))
                 .ReturnsAsync("Admin");

            var result = await _sut.CreateAsync(req);

            Assert.That(result.Email, Is.EqualTo("Ada@Example.Com"));
            Assert.That(result.RoleName, Is.EqualTo("Admin"));
        }

        [Test]
        public void CreateAsync_Throws_WhenEmailExists()
        {
            var req = new CreateUserRequest(
                FirstName: "Ada",
                LastName: "Lovelace",
                Email: "Ada@Example.Com",
                Phone: null,
                RoleId: 1,
                Password: "p@ss",
                ClientReferenceId: null,
                DeptId: null
            );

            _repo.Setup(r => r.EmailExistsWithRoleAsync("ada@example.com", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

            Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync(req));
        }

        [Test]
        public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
        {
            _repo.Setup(r => r.GetByIdWithRoleNoTrackingAsync(123, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((User?)null);

            var result = await _sut.GetByIdAsync(123);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task GetByIdAsync_ReturnsResponse_WhenFound()
        {
            var user = new User
            {
                Id = 7,
                FirstName = "Grace",
                LastName = "Hopper",
                Email = "grace@example.com",
                RoleId = 2
            };

            _repo.Setup(r => r.GetByIdWithRoleNoTrackingAsync(7, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(user);

            _repo.Setup(r => r.GetRoleNameByIdAsync(2, It.IsAny<CancellationToken>()))
                 .ReturnsAsync("Manager");

            var res = await _sut.GetByIdAsync(7);
            Assert.That(res, Is.Not.Null);
            Assert.That(res!.RoleName, Is.EqualTo("Manager"));
        }

        [Test]
        public async Task GetAllAsync_Maps_Results()
        {
            var list = new List<User>
            {
                new() { Id = 1, FirstName = "A", LastName = "A", Email = "a@x.com", RoleId = 1 },
                new() { Id = 2, FirstName = "B", LastName = "B", Email = "b@x.com", RoleId = 2 },
            };

            _repo.Setup(r => r.GetAllWithRoleNoTrackingAsync(false, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(list);

            var res = await _sut.GetAllAsync();
            Assert.That(res.Count, Is.EqualTo(2));
            Assert.That(res[0].Id, Is.EqualTo(1));
        }

        [Test]
        public async Task UpdateAsync_ReturnsNull_WhenNotFound()
        {
            _repo.Setup(r => r.GetByIdTrackedAsync(55, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((User?)null);

            var req = new UpdateUserRequest(
                FirstName: "New",
                LastName: null,
                Phone: null,
                RoleId: null,
                ClientReferenceId: null,
                Password: null,
                DeptId: null
            );

            var res = await _sut.UpdateAsync(55, req);
            Assert.That(res, Is.Null);
        }

        [Test]
        public async Task UpdateAsync_Persists_AndMaps()
        {
            var user = new User
            {
                Id = 5,
                FirstName = "Old",
                LastName = "Name",
                Email = "old@example.com",
                RoleId = 1
            };

            _repo.Setup(r => r.GetByIdTrackedAsync(5, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(user);

            _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(1);

            _repo.Setup(r => r.GetRoleNameByIdAsync(1, It.IsAny<CancellationToken>()))
                 .ReturnsAsync("Admin");

            var req = new UpdateUserRequest(
                FirstName: "New",
                LastName: null,
                Phone: "123",
                RoleId: null,
                ClientReferenceId: null,
                Password: null,
                DeptId: null
            );

            var res = await _sut.UpdateAsync(5, req);

            Assert.That(res, Is.Not.Null);
            Assert.That(res!.FirstName, Is.EqualTo("New"));
            Assert.That(res.RoleName, Is.EqualTo("Admin"));
        }

        [Test]
        public async Task DeleteAsync_ReturnsFlag_FromRepository()
        {
            _repo.Setup(r => r.RemoveByIdAsync(9, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

            var ok = await _sut.DeleteAsync(9);
            Assert.That(ok, Is.True);
        }

        [Test]
        public async Task GetByEmailAsync_Normalizes_AndMaps()
        {
            var user = new User
            {
                Id = 8,
                FirstName = "Mail",
                LastName = "User",
                Email = "mail@x.com",
                RoleId = 3
            };

            _repo.Setup(r => r.GetByEmailWithRoleNoTrackingAsync("mail@x.com", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(user);

            var res = await _sut.GetByEmailAsync("  MAIL@X.COM ");
            Assert.That(res, Is.Not.Null);
            Assert.That(res!.Email, Is.EqualTo("mail@x.com").Or.EqualTo("MAIL@X.COM"));
        }
    }
}
