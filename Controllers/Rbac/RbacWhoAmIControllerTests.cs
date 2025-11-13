// UserTest/Controllers/Rbac/RbacWhoAmIControllerTests.cs
using IDV_Backend.Authorization;
using IDV_Backend.Contracts.Rbac;
using IDV_Backend.Controllers.Rbac;
using IDV_Backend.Models.Roles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using System.Linq;
using System.Security.Claims;

namespace UserTest.Controllers.Rbac;

public class RbacWhoAmIControllerTests
{
    private static DefaultHttpContext MakeHttpContext(ClaimsPrincipal? principal = null)
    {
        var ctx = new DefaultHttpContext();
        if (principal != null)
            ctx.User = principal;
        return ctx;
    }

    [Test]
    public void Returns_Unauthorized_For_Anonymous()
    {
        var controller = new RbacWhoAmIController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = MakeHttpContext() // no user
            }
        };

        var result = controller.GetEffective();
        Assert.That(result.Result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public void Returns_Effective_Rbac_For_Authenticated_User()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "123"),
            new Claim(ClaimTypes.Email, "jane.doe@example.com"),
            new Claim(ClaimTypes.Role, "WorkflowAdmin"),
            new Claim(AuthClaimTypes.Permission, PermissionCodes.ConfigureRbac),
            new Claim(AuthClaimTypes.Permission, PermissionCodes.ViewRespondVerifs),
            new Claim(RbacClaimTypes.RolesVersion, "7"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var controller = new RbacWhoAmIController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = MakeHttpContext(principal)
            }
        };

        var action = controller.GetEffective();
        Assert.That(action.Result, Is.Null); // should be Ok(...) not Unauthorized
        var ok = action.Value!;
        Assert.That(ok, Is.Not.Null);
        Assert.That(ok.UserId, Is.EqualTo(123));
        Assert.That(ok.Email, Is.EqualTo("jane.doe@example.com"));
        Assert.That(ok.Role, Is.EqualTo("WorkflowAdmin"));
        Assert.That(ok.RolesVersion, Is.EqualTo(7));
        Assert.That(ok.Permissions, Has.Length.EqualTo(2));
        Assert.That(ok.Permissions, Does.Contain(PermissionCodes.ConfigureRbac));
        Assert.That(ok.Permissions, Does.Contain(PermissionCodes.ViewRespondVerifs));
    }

    [Test]
    public void Handles_Missing_Optional_Claims()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "999"),
            // no email claim
            new Claim(ClaimTypes.Name, "fallback@example.com"),
            // no role
            // no permissions
            // no roles_ver
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var controller = new RbacWhoAmIController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = MakeHttpContext(principal)
            }
        };

        var result = controller.GetEffective();
        var dto = result.Value!;
        Assert.That(dto.UserId, Is.EqualTo(999));
        Assert.That(dto.Email, Is.EqualTo("fallback@example.com"));
        Assert.That(dto.Role, Is.EqualTo(string.Empty));
        Assert.That(dto.Permissions, Is.Empty);
        Assert.That(dto.RolesVersion, Is.EqualTo(0));
    }
}
