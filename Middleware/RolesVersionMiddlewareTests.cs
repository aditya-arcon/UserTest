using System.Security.Claims;
using System.Threading.Tasks;
using IDV_Backend.Middleware;
using IDV_Backend.Services.Roles;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;

namespace UserTest.Middleware;

public class RolesVersionGuardMiddlewareTests
{
    [Test]
    public async Task Allows_Request_When_Token_Version_Matches()
    {
        var versionSvc = new Mock<IRolesVersionService>();
        versionSvc.Setup(s => s.GetAsync(default)).ReturnsAsync(3);

        var middleware = new RolesVersionGuardMiddleware(async ctx =>
        {
            // success path: do nothing
            await Task.CompletedTask;
        });

        var ctx = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("roles_ver", "3")
        }, "TestAuth");

        ctx.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(ctx, versionSvc.Object);
        Assert.That(ctx.Response.StatusCode, Is.EqualTo(200)); // default remains 200
    }

    [Test]
    public async Task Rejects_When_Token_Version_Is_Stale()
    {
        var versionSvc = new Mock<IRolesVersionService>();
        versionSvc.Setup(s => s.GetAsync(default)).ReturnsAsync(5);

        var middleware = new RolesVersionGuardMiddleware(_ => Task.CompletedTask);

        var ctx = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("roles_ver", "3")
        }, "TestAuth");

        ctx.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(ctx, versionSvc.Object);

        Assert.That(ctx.Response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
        // Accept the charset that WriteAsJsonAsync appends
        Assert.That(ctx.Response.ContentType, Does.StartWith("application/json"));
    }

    [Test]
    public async Task Allows_Anonymous()
    {
        var versionSvc = new Mock<IRolesVersionService>();
        versionSvc.Setup(s => s.GetAsync(default)).ReturnsAsync(42);

        var middleware = new RolesVersionGuardMiddleware(_ => Task.CompletedTask);

        var ctx = new DefaultHttpContext();
        // No user identity

        await middleware.InvokeAsync(ctx, versionSvc.Object);

        Assert.That(ctx.Response.StatusCode, Is.EqualTo(200));
    }
}
