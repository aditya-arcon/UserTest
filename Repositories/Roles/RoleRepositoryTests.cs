using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Repositories.Roles;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.Roles
{
    [TestFixture]
    public class RoleRepositoryTests
    {
        private static ApplicationDbContext NewContext()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"roles_repo_{System.Guid.NewGuid()}")
                .Options;

            var ctx = new ApplicationDbContext(opts);
            // IMPORTANT: triggers seeding from HasData for InMemory provider
            ctx.Database.EnsureCreated();
            return ctx;
        }

        [Test]
        public async Task ExistsByNameAsync_ReturnsTrue_ForSeededRole()
        {
            await using var db = NewContext();
            var repo = new RoleRepository(db);

            var exists = await repo.ExistsByNameAsync(RoleName.Admin, CancellationToken.None);
            Assert.That(exists, Is.True);
        }

        [Test]
        public async Task ExistsByNameAsync_ReturnsFalse_ForMissingRole()
        {
            await using var db = NewContext();
            var repo = new RoleRepository(db);

            var missing = (RoleName)999;
            var exists = await repo.ExistsByNameAsync(missing, CancellationToken.None);
            Assert.That(exists, Is.False);
        }

        [Test]
        public async Task ExistsByNameOtherIdAsync_IgnoresSameId_AndDetectsCollisionOnOtherId()
        {
            await using var db = NewContext();
            var repo = new RoleRepository(db);

            var sameIdNoCollision = await repo.ExistsByNameOtherIdAsync(RoleName.Admin, exceptId: 2, CancellationToken.None);
            Assert.That(sameIdNoCollision, Is.False, "Should not count same id as a collision");

            var collides = await repo.ExistsByNameOtherIdAsync(RoleName.Admin, exceptId: 999, CancellationToken.None);
            Assert.That(collides, Is.True, "Admin exists under a different id, should collide");
        }

        [Test]
        public async Task GetIdByNameAsync_ReturnsSeededId()
        {
            await using var db = NewContext();
            var repo = new RoleRepository(db);

            var id = await repo.GetIdByNameAsync(RoleName.SupportAdmin, CancellationToken.None);
            Assert.That(id, Is.EqualTo(7));
        }

        [Test]
        public async Task GetAllNoTrackingAsync_ReturnsSeededRoles_InOrderById()
        {
            await using var db = NewContext();
            var repo = new RoleRepository(db);

            var items = await repo.GetAllNoTrackingAsync(CancellationToken.None);
            Assert.That(items, Is.Not.Empty);
            Assert.That(items.First().Id, Is.EqualTo(1));  // SuperAdmin
            Assert.That(items.Last().Id, Is.EqualTo(9));   // IntegrationAdmin
        }
    }
}
