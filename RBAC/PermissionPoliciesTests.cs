// UserTest/RBAC/PermissionPoliciesTests.cs
using System.Security.Claims;
using IDV_Backend.Authorization;
using IDV_Backend.Models.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace UserTest.RBAC;

[TestFixture]
public class PermissionPoliciesTests
{
    private ServiceProvider _sp = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationPolicies(); // registers our policies
        _sp = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() => _sp?.Dispose();

    [Test]
    public async Task PermPolicy_Succeeds_When_Claim_Present()
    {
        var auth = _sp.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AuthClaimTypes.Permission, PermissionCodes.ViewRespondVerifs)
        }, "test"));

        var result = await auth.AuthorizeAsync(user, null, $"Perm:{PermissionCodes.ViewRespondVerifs}");
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task PermPolicy_Fails_When_Claim_Missing()
    {
        var auth = _sp.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AuthClaimTypes.Permission, PermissionCodes.ManageUsersAndRoles)
        }, "test"));

        var result = await auth.AuthorizeAsync(user, null, $"Perm:{PermissionCodes.ViewRespondVerifs}");
        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task RolePolicies_Work()
    {
        var auth = _sp.GetRequiredService<IAuthorizationService>();

        var adminUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Admin")
        }, "test"));

        var superUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "SuperAdmin")
        }, "test"));

        var res1 = await auth.AuthorizeAsync(adminUser, null, Policies.AdminOnly);
        var res2 = await auth.AuthorizeAsync(superUser, null, Policies.SuperAdminOnly);
        var res3 = await auth.AuthorizeAsync(adminUser, null, Policies.AdminOrSuperAdmin);
        var res4 = await auth.AuthorizeAsync(superUser, null, Policies.AdminOrSuperAdmin);

        Assert.Multiple(() =>
        {
            Assert.That(res1.Succeeded, Is.True);
            Assert.That(res2.Succeeded, Is.True);
            Assert.That(res3.Succeeded, Is.True);
            Assert.That(res4.Succeeded, Is.True);
        });
    }
}
