using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Data;
using IDV_Backend.Models.Roles;
using IDV_Backend.Repositories.Roles;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.Roles
{
    [TestFixture]
    public class PermissionRepositoryTests
    {
        private static ApplicationDbContext NewContext()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"perms_repo_{System.Guid.NewGuid()}")
                .Options;

            var ctx = new ApplicationDbContext(opts);
            // IMPORTANT: triggers seeding from HasData for InMemory provider
            ctx.Database.EnsureCreated();
            return ctx;
        }

        [Test]
        public async Task GetAllAsync_ReturnsSeededNine()
        {
            await using var db = NewContext();
            var repo = new PermissionRepository(db);

            var all = await repo.GetAllAsync(CancellationToken.None);
            Assert.That(all.Count, Is.EqualTo(9));
            Assert.That(all.Any(p => p.Code == PermissionCodes.ManageUsersAndRoles), Is.True);
        }

        [Test]
        public async Task GetByCodesAsync_TrimsAndDeduplicates_AndIsCaseSensitive()
        {
            await using var db = NewContext();
            var repo = new PermissionRepository(db);

            var codes = new[]
            {
                "  " + PermissionCodes.ViewRespondVerifs + "  ",
                PermissionCodes.ViewRespondVerifs,
                PermissionCodes.ManageSupportTickets
            };

            var list = await repo.GetByCodesAsync(codes, CancellationToken.None);

            Assert.That(list.Count, Is.EqualTo(2));
            var returned = list.Select(p => p.Code).OrderBy(c => c).ToArray();
            Assert.That(returned, Is.EquivalentTo(new[]
            {
                PermissionCodes.ManageSupportTickets,
                PermissionCodes.ViewRespondVerifs
            }));

            var miss = await repo.GetByCodesAsync(new[] { PermissionCodes.ViewRespondVerifs.ToLowerInvariant() }, CancellationToken.None);
            Assert.That(miss.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task GetCodesByRoleIdAsync_ReturnsSortedCodes_ForSeededMatrix()
        {
            await using var db = NewContext();
            var repo = new PermissionRepository(db);

            var codes = await repo.GetCodesByRoleIdAsync(roleId: 7, CancellationToken.None);
            Assert.That(codes, Is.Not.Empty);

            Assert.That(codes, Is.EquivalentTo(new[]
            {
                PermissionCodes.ManageSupportTickets,
                PermissionCodes.ManualOverrideReview,
                PermissionCodes.ViewRespondVerifs
            }));
        }

        [Test]
        public async Task RoleExistsAsync_WorksForExistingAndMissing()
        {
            await using var db = NewContext();
            var repo = new PermissionRepository(db);

            Assert.That(await repo.RoleExistsAsync(1, CancellationToken.None), Is.True);
            Assert.That(await repo.RoleExistsAsync(999, CancellationToken.None), Is.False);
        }

        [Test]
        public async Task ReplaceRolePermissionsAsync_ReplacesAtomically_AndIsIdempotent()
        {
            await using var db = NewContext();
            var repo = new PermissionRepository(db);

            var roleId = 4L;

            var initial = await repo.GetCodesByRoleIdAsync(roleId, CancellationToken.None);
            Assert.That(initial, Is.EquivalentTo(new[]
            {
                PermissionCodes.CreateEditWorkflows,
                PermissionCodes.ViewRespondVerifs
            }));

            await repo.ReplaceRolePermissionsAsync(roleId, new long[] { 1, 3 }, CancellationToken.None);

            var afterFirst = await repo.GetCodesByRoleIdAsync(roleId, CancellationToken.None);
            Assert.That(afterFirst, Is.EquivalentTo(new[]
            {
                PermissionCodes.ManageUsersAndRoles,
                PermissionCodes.ConfigureRbac
            }));

            await repo.ReplaceRolePermissionsAsync(roleId, new long[] { 1, 3 }, CancellationToken.None);

            var afterSecond = await repo.GetCodesByRoleIdAsync(roleId, CancellationToken.None);
            Assert.That(afterSecond, Is.EquivalentTo(new[]
            {
                PermissionCodes.ManageUsersAndRoles,
                PermissionCodes.ConfigureRbac
            }));

            var countRows = await db.RolePermissions
                .Where(rp => rp.RoleId == roleId)
                .CountAsync();
            Assert.That(countRows, Is.EqualTo(2));
        }
    }
}
