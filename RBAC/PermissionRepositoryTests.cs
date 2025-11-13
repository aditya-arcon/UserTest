using IDV_Backend.Data;
using IDV_Backend.Models.Roles;
using IDV_Backend.Repositories.Roles;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace UserTest.RBAC;

[TestFixture]
public class PermissionRepositoryTests
{
    private ApplicationDbContext _db = null!;
    private PermissionRepository _repo = null!;

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(opts);

        // IMPORTANT: EnsureCreated applies HasData seeds from your configurations.
        _db.Database.EnsureCreated();

        // Do NOT manually add permissions/roles here — they’re already seeded.
        _repo = new PermissionRepository(_db);
    }

    [Test]
    public async Task GetByCodes_ReturnsSeededPermissions()
    {
        var wanted = new[] { PermissionCodes.ManageUsersAndRoles, PermissionCodes.ViewRespondVerifs };
        var items = await _repo.GetByCodesAsync(wanted);
        CollectionAssert.AreEquivalent(wanted, items.Select(i => i.Code).ToArray());
    }
}
