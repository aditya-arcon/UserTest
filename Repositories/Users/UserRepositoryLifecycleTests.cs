using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.User;
using IDV_Backend.Repositories.Users;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.Users
{
    [TestFixture]
    public class UserRepositoryLifecycleTests
    {
        private ApplicationDbContext _db = null!;
        private IUserRepository _repo = null!;

        [SetUp]
        public void SetUp()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
                .Options;

            _db = new ApplicationDbContext(opts);

            // Seed a role to satisfy FK
            _db.Roles.Add(new RoleUserMapping { Id = 1, RoleName = RoleName.User });
            _db.SaveChanges();

            _repo = new UserRepository(_db);
        }

        [TearDown]
        public void TearDown() => _db.Dispose();

        [Test]
        public async Task SoftDeprovision_SetsFlags_AndTimestamps()
        {
            // arrange
            var u = new User
            {
                FirstName = "Soft",
                LastName = "Deprov",
                Email = "soft@idv.local",
                RoleId = 1,
                ClientReferenceId = 1001,
                PublicId = 2001,
                PasswordHash = "hash",
                IsActive = true
            };
            await _repo.AddUserAsync(u, CancellationToken.None);
            await _repo.SaveChangesAsync(CancellationToken.None);

            // act
            var ok = await _repo.SoftDeprovisionAsync(u.Id, "Left org", adminId: 999, CancellationToken.None);

            // assert
            Assert.That(ok, Is.True);

            var again = await _repo.GetByIdTrackedAsync(u.Id, CancellationToken.None);
            Assert.That(again, Is.Not.Null);
            Assert.That(again!.IsActive, Is.False);
            Assert.That(again.DeprovisionReason, Is.EqualTo("Left org"));
            Assert.That(again.DeprovisionedBy, Is.EqualTo(999));
            Assert.That(again.DeprovisionedAt, Is.Not.Null);
        }

        [Test]
        public async Task SoftDeprovision_ReturnsFalse_WhenUserMissing()
        {
            var ok = await _repo.SoftDeprovisionAsync(99999, "reason", 1, CancellationToken.None);
            Assert.That(ok, Is.False);
        }

        [Test]
        public async Task Reprovision_ClearsFlags_AndReactivates()
        {
            // arrange: create inactive user
            var u = new User
            {
                FirstName = "Re",
                LastName = "Activate",
                Email = "re@idv.local",
                RoleId = 1,
                ClientReferenceId = 333,
                PublicId = 444,
                PasswordHash = "h",
                IsActive = false,
                DeprovisionReason = "temp",
                DeprovisionedBy = 42,
                DeprovisionedAt = DateTime.UtcNow.AddHours(-1)
            };
            await _repo.AddUserAsync(u, CancellationToken.None);
            await _repo.SaveChangesAsync(CancellationToken.None);

            // act
            var ok = await _repo.ReprovisionAsync(u.Id, CancellationToken.None);

            // assert
            Assert.That(ok, Is.True);

            var again = await _repo.GetByIdTrackedAsync(u.Id, CancellationToken.None);
            Assert.That(again, Is.Not.Null);
            Assert.That(again!.IsActive, Is.True);
            Assert.That(again.DeprovisionedAt, Is.Null);
            Assert.That(again.DeprovisionedBy, Is.Null);
            Assert.That(again.DeprovisionReason, Is.Null);
        }

        [Test]
        public async Task Reprovision_ReturnsFalse_WhenUserMissing()
        {
            var ok = await _repo.ReprovisionAsync(404040, CancellationToken.None);
            Assert.That(ok, Is.False);
        }

        [Test]
        public async Task HardDelete_RemovesUser()
        {
            // arrange
            var u = new User
            {
                FirstName = "Hard",
                LastName = "Delete",
                Email = "hard@idv.local",
                RoleId = 1,
                ClientReferenceId = 555,
                PublicId = 666,
                PasswordHash = "h"
            };
            await _repo.AddUserAsync(u, CancellationToken.None);
            await _repo.SaveChangesAsync(CancellationToken.None);

            // act
            var ok = await _repo.RemoveByIdAsync(u.Id, CancellationToken.None);

            // assert
            Assert.That(ok, Is.True);

            var missing = await _repo.GetByIdTrackedAsync(u.Id, CancellationToken.None);
            Assert.That(missing, Is.Null);
        }
    }
}
