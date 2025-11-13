using FluentValidation;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Contracts.Roles.Validators;
using IDV_Backend.Data;
using IDV_Backend.Models.Roles;
using IDV_Backend.Repositories.Roles;
using IDV_Backend.Services.Roles; // IRolesVersionService, RoleService
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace UserTest.RBAC;

[TestFixture]
public class RoleService_SetPermissions_ByCodes_Tests
{
    private ApplicationDbContext _db = null!;
    private IRoleRepository _roles = null!;
    private IPermissionRepository _perms = null!;
    private IValidator<CreateRoleRequest> _createVal = null!;
    private IValidator<UpdateRoleRequest> _updateVal = null!;
    private IValidator<UpdateRolePermissionsRequest> _permVal = null!;
    private RoleService _svc = null!;
    private IRolesVersionService _rolesVersion = null!;

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(opts);
        _db.Database.EnsureCreated(); // apply model + HasData

        _roles = new RoleRepository(_db);
        _perms = new PermissionRepository(_db);

        _createVal = Mock.Of<IValidator<CreateRoleRequest>>();
        _updateVal = Mock.Of<IValidator<UpdateRoleRequest>>();
        _permVal = new UpdateRolePermissionsRequestValidator();

        // Loose mock; no strict signature assumptions
        _rolesVersion = new Mock<IRolesVersionService>(MockBehavior.Loose).Object;

        // FIX: Pass the required 'rolesVersion' argument to RoleService constructor
        _svc = new RoleService(_roles, _perms, _createVal, _updateVal, _permVal, _rolesVersion);
    }

    [Test]
    public async Task SetPermissions_SetsByCodes_AndReturnsCodes()
    {
        // Admin role is seeded with Id=2 via RoleUserMappingConfiguration
        var codes = new[] { PermissionCodes.ManageUsersAndRoles, PermissionCodes.ViewRespondVerifs };

        var res = await _svc.SetPermissionsAsync(2, new UpdateRolePermissionsRequest(codes));

        Assert.That(res.RoleId, Is.EqualTo(2));
        Assert.That(res.Codes, Is.EquivalentTo(codes));

        var roundTrip = await _svc.GetPermissionsAsync(2);
        Assert.That(roundTrip!.Codes, Is.EquivalentTo(codes));
    }

    [Test]
    public void SetPermissions_UnknownCode_Throws()
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _svc.SetPermissionsAsync(2, new UpdateRolePermissionsRequest(new[] { "Nope" })));
        Assert.That(ex!.Message, Does.Contain("do not exist"));
    }
}
