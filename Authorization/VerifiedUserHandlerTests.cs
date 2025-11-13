using System.Security.Claims;
using System.Threading.Tasks;
using IDV_Backend.Authorization.Handlers;
using IDV_Backend.Authorization.Requirements;
using IDV_Backend.Services.Verification;
using Microsoft.AspNetCore.Authorization;
using Moq;
using NUnit.Framework;

namespace UserTest.Authorization
{
    public class VerifiedUserHandlerTests
    {
        [Test]
        public async Task AdminRole_IsTreatedAsVerified()
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "admin@x.com"),
                new Claim(ClaimTypes.Role, "Admin")
            }, "jwt"));

            var req = new VerifiedUserRequirement();
            var svc = new Mock<IVerifiedUserService>();
            svc.Setup(s => s.IsVerifiedAsync(user, default)).ReturnsAsync(true); // handler uses service anyway

            var handler = new VerifiedUserHandler(svc.Object);
            var ctx = new AuthorizationHandlerContext(new[] { req }, user, null);

            await handler.HandleAsync(ctx);

            Assert.That(ctx.HasSucceeded, Is.True, "Admin should satisfy verified requirement.");
        }

        [Test]
        public async Task ExplicitVerifiedClaim_True_Succeeds()
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("verified", "true")
            }, "jwt"));

            var req = new VerifiedUserRequirement();
            var svc = new Mock<IVerifiedUserService>();
            svc.Setup(s => s.IsVerifiedAsync(user, default)).ReturnsAsync(true);

            var handler = new VerifiedUserHandler(svc.Object);
            var ctx = new AuthorizationHandlerContext(new[] { req }, user, null);

            await handler.HandleAsync(ctx);

            Assert.That(ctx.HasSucceeded, Is.True);
        }

        [Test]
        public async Task NotVerified_Fails()
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[0], "jwt"));

            var req = new VerifiedUserRequirement();
            var svc = new Mock<IVerifiedUserService>();
            svc.Setup(s => s.IsVerifiedAsync(user, default)).ReturnsAsync(false);

            var handler = new VerifiedUserHandler(svc.Object);
            var ctx = new AuthorizationHandlerContext(new[] { req }, user, null);

            await handler.HandleAsync(ctx);

            Assert.That(ctx.HasSucceeded, Is.False);
        }
    }
}
