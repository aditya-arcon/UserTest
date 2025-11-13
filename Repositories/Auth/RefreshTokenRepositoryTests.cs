using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.User;
using IDV_Backend.Repositories.Auth;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace UserTest.Repositories.Auth
{
    [TestFixture]
    public class RefreshTokenRepositoryTests
    {
        private ApplicationDbContext _db = default!;
        private IRefreshTokenRepository _repo = default!;

        [SetUp]
        public void SetUp()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
                .Options;
            _db = new ApplicationDbContext(opts);

            // Seed a user so FK exists
            _db.Roles.Add(new RoleUserMapping { Id = 1, RoleName = RoleName.User });
            _db.Users.Add(new User
            {
                Id = 123, // explicit id so we can reference
                FirstName = "Test",
                LastName = "User",
                Email = "t@idv.local",
                RoleId = 1,
                ClientReferenceId = 1001,
                PublicId = 2002,
                PasswordHash = "hash"
            });
            _db.SaveChanges();

            _repo = new RefreshTokenRepository(_db);
        }

        [TearDown]
        public void TearDown() => _db.Dispose();

        [Test]
        public async Task Add_Then_GetByToken_Works()
        {
            var t = new RefreshToken
            {
                UserId = 123,
                Token = "REFRESH-1",
                ExpiresAt = System.DateTime.UtcNow.AddDays(7)
            };

            await _repo.AddAsync(t, CancellationToken.None);
            var saved = await _repo.SaveChangesAsync(CancellationToken.None);
            Assert.That(saved, Is.GreaterThan(0));

            var got = await _repo.GetByTokenAsync("REFRESH-1", CancellationToken.None);
            Assert.That(got, Is.Not.Null);
            Assert.That(got!.UserId, Is.EqualTo(123));
            Assert.That(got.User, Is.Not.Null);
            Assert.That(got.User!.Email, Is.EqualTo("t@idv.local"));
        }

        [Test]
        public async Task GetByToken_Unknown_ReturnsNull()
        {
            var got = await _repo.GetByTokenAsync("nope", CancellationToken.None);
            Assert.That(got, Is.Null);
        }

        [Test]
        public async Task SaveChanges_Returns_Zero_When_NoChanges()
        {
            var count = await _repo.SaveChangesAsync(CancellationToken.None);
            Assert.That(count, Is.EqualTo(0));
        }
    }
}
