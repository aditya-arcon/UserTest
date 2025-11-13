// UserTest/Controllers/RbacMaintenanceControllerTests.cs
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Authorization;
using IDV_Backend.Controllers.Rbac;
using IDV_Backend.Models.AdminLogs;
using IDV_Backend.Services.AdminLogs;
using IDV_Backend.Services.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace UserTest.Controllers;

public class RbacMaintenanceControllerTests
{
    [Test]
    public void Controller_Has_RequireAdmin_AuthorizeAttribute()
    {
        var attr = typeof(RbacMaintenanceController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .FirstOrDefault();

        Assert.That(attr, Is.Not.Null, "AuthorizeAttribute should be present on controller.");
        Assert.That(attr!.Policy, Is.EqualTo(Policies.RequireAdmin));
    }

    [Test]
    public async Task GetVersion_Returns_Current_Version()
    {
        var rolesSvc = new Mock<IRolesVersionService>();
        var adminLog = new Mock<IAdminActionLogger>();

        rolesSvc.Setup(s => s.GetAsync(default)).ReturnsAsync(7);

        var sut = new RbacMaintenanceController(rolesSvc.Object, adminLog.Object);

        var res = await sut.GetVersion(CancellationToken.None) as OkObjectResult;
        Assert.That(res, Is.Not.Null);
        dynamic body = res!.Value!;
        Assert.That((int)body.version, Is.EqualTo(7));
    }

    [Test]
    public async Task InvalidateTokens_Bumps_Version_And_Logs()
    {
        var rolesSvc = new Mock<IRolesVersionService>();
        var adminLog = new Mock<IAdminActionLogger>();

        rolesSvc.Setup(s => s.BumpAsync(default)).ReturnsAsync(12);

        var sut = new RbacMaintenanceController(rolesSvc.Object, adminLog.Object);

        var res = await sut.InvalidateTokens(CancellationToken.None) as OkObjectResult;
        Assert.That(res, Is.Not.Null);

        dynamic body = res!.Value!;
        Assert.That((int)body.version, Is.EqualTo(12));
        Assert.That((string)body.message, Does.Contain("stale"));

        adminLog.Verify(l => l.LogSuccessAsync(
                AdminAction.RoleUpdate,
                "RBAC",
                12,
                null,
                It.Is<string>(s => s.Contains("RBAC tokens invalidated")),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        rolesSvc.Verify(s => s.BumpAsync(default), Times.Once);
    }
}
