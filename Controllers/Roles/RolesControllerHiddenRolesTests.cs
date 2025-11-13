// UserTest/Controllers/Roles/RolesControllerHiddenRolesTests.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Controllers.Roles;
using IDV_Backend.Services.AdminLogs;
using IDV_Backend.Services.Roles;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace UserTest.Controllers.Roles;

public class RolesControllerHiddenRolesTests
{
    [Test]
    public async Task GetAll_Default_Hides_SupportAdmin()
    {
        var visible = new List<RoleDto>
        {
            new(1, "SuperAdmin"),
            new(2, "Admin"),
            new(3, "User")
        };
        var all = visible.Concat(new[] { new RoleDto(7, "SupportAdmin") }).ToList();

        var svc = new Mock<IRoleService>();
        svc.Setup(s => s.GetAllAsync(false, It.IsAny<CancellationToken>()))
           .ReturnsAsync(visible);
        svc.Setup(s => s.GetAllAsync(true, It.IsAny<CancellationToken>()))
           .ReturnsAsync(all);

        var logger = new Mock<IAdminActionLogger>();
        var ctrl = new RolesController(svc.Object, logger.Object);

        var res = await ctrl.GetAll(includeHidden: false, CancellationToken.None) as OkObjectResult;
        Assert.That(res, Is.Not.Null);
        var list = (IEnumerable<RoleDto>)res!.Value!;
        Assert.That(list.Any(r => r.Name == "SupportAdmin"), Is.False);
        Assert.That(list.Count(), Is.EqualTo(3));
    }

    [Test]
    public async Task GetAll_With_IncludeHidden_Returns_All()
    {
        var visible = new List<RoleDto>
        {
            new(1, "SuperAdmin"),
            new(2, "Admin"),
            new(3, "User")
        };
        var all = visible.Concat(new[] { new RoleDto(7, "SupportAdmin") }).ToList();

        var svc = new Mock<IRoleService>();
        svc.Setup(s => s.GetAllAsync(true, It.IsAny<CancellationToken>()))
           .ReturnsAsync(all);

        var logger = new Mock<IAdminActionLogger>();
        var ctrl = new RolesController(svc.Object, logger.Object);

        var res = await ctrl.GetAll(includeHidden: true, CancellationToken.None) as OkObjectResult;
        Assert.That(res, Is.Not.Null);
        var list = (IEnumerable<RoleDto>)res!.Value!;
        Assert.That(list.Any(r => r.Name == "SupportAdmin"), Is.True);
        Assert.That(list.Count(), Is.EqualTo(4));
    }
}
