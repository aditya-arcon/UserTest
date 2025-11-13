// UserTest/Services/Roles/HiddenRolesServiceTests.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Models;
using IDV_Backend.Repositories.Roles;
using IDV_Backend.Services.Roles;
using Moq;
using NUnit.Framework;

namespace UserTest.Services.Roles;

public class HiddenRolesServiceTests
{
    [Test]
    public async Task GetAllAsync_Filters_SupportAdmin_By_Default()
    {
        var roles = new[]
        {
            new RoleUserMapping { Id = 1, RoleName = RoleName.SuperAdmin },
            new RoleUserMapping { Id = 2, RoleName = RoleName.Admin },
            new RoleUserMapping { Id = 3, RoleName = RoleName.User },
            new RoleUserMapping { Id = 7, RoleName = RoleName.SupportAdmin },
        };

        var repo = new Mock<IRoleRepository>();
        repo.Setup(r => r.GetAllNoTrackingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles.ToList());

        var perms = new Mock<IPermissionRepository>();
        var createVal = new Mock<IValidator<CreateRoleRequest>>();
        var updateVal = new Mock<IValidator<UpdateRoleRequest>>();
        var permVal = new Mock<IValidator<UpdateRolePermissionsRequest>>();
        var verSvc = new Mock<IRolesVersionService>();

        var svc = new RoleService(repo.Object, perms.Object, createVal.Object, updateVal.Object, permVal.Object, verSvc.Object);

        var resultDefault = (await svc.GetAllAsync()).ToArray();
        Assert.That(resultDefault.Any(r => r.Name == RoleName.SupportAdmin.ToString()), Is.False);
        Assert.That(resultDefault.Select(r => r.Name), Does.Contain(RoleName.SuperAdmin.ToString()));
        Assert.That(resultDefault.Select(r => r.Name), Does.Contain(RoleName.Admin.ToString()));
        Assert.That(resultDefault.Select(r => r.Name), Does.Contain(RoleName.User.ToString()));
    }

    [Test]
    public async Task GetAllAsync_IncludingHidden_Returns_SupportAdmin()
    {
        var roles = new[]
        {
            new RoleUserMapping { Id = 1, RoleName = RoleName.SuperAdmin },
            new RoleUserMapping { Id = 7, RoleName = RoleName.SupportAdmin },
        };

        var repo = new Mock<IRoleRepository>();
        repo.Setup(r => r.GetAllNoTrackingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles.ToList());

        var perms = new Mock<IPermissionRepository>();
        var createVal = new Mock<IValidator<CreateRoleRequest>>();
        var updateVal = new Mock<IValidator<UpdateRoleRequest>>();
        var permVal = new Mock<IValidator<UpdateRolePermissionsRequest>>();
        var verSvc = new Mock<IRolesVersionService>();

        var svc = new RoleService(repo.Object, perms.Object, createVal.Object, updateVal.Object, permVal.Object, verSvc.Object);

        var all = (await svc.GetAllAsync(includeHidden: true)).ToArray();
        Assert.That(all.Any(r => r.Name == RoleName.SupportAdmin.ToString()), Is.True);
        Assert.That(all.Any(r => r.Name == RoleName.SuperAdmin.ToString()), Is.True);
    }
}
