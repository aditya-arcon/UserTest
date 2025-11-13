using System.Security.Claims;
using IDV_Backend.Authorization;
using IDV_Backend.Models.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace UserTest.Authorization
{
    public class PermissionPolicyEvaluationTests
    {
        private ServiceProvider _sp = null!;

        [SetUp]
        public void Setup()
        {
            var services = new ServiceCollection();

            // Add logging because DefaultAuthorizationService requires ILogger<DefaultAuthorizationService>
            services.AddLogging(builder => builder.AddDebug().AddConsole());

            // Core auth + our permission/role policies
            services.AddAuthorization();
            IDV_Backend.Authorization.AuthorizationExtensions.AddAuthorizationPolicies(services);

            _sp = services.BuildServiceProvider();
        }

        [TearDown]
        public void TearDown() => _sp.Dispose();

        [Test]
        public void Principal_With_Matching_Permission_Succeeds()
        {
            var auth = _sp.GetRequiredService<IAuthorizationService>();
            var code = PermissionCodes.ViewRespondVerifs;

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(AuthClaimTypes.Permission, code)
            }, "test");
            var user = new ClaimsPrincipal(identity);

            var result = auth.AuthorizeAsync(user, resource: null, policyName: $"Perm:{code}").Result;
            Assert.That(result.Succeeded, Is.True, "Principal with matching permission should succeed.");
        }

        [Test]
        public void Principal_Without_Matching_Permission_Fails()
        {
            var auth = _sp.GetRequiredService<IAuthorizationService>();
            var code = PermissionCodes.ManageUsersAndRoles;

            var identity = new ClaimsIdentity(new[]
            {
                // wrong permission on purpose
                new Claim(AuthClaimTypes.Permission, PermissionCodes.ViewRespondVerifs)
            }, "test");
            var user = new ClaimsPrincipal(identity);

            var result = auth.AuthorizeAsync(user, resource: null, policyName: $"Perm:{code}").Result;
            Assert.That(result.Succeeded, Is.False, "Principal without permission should fail.");
        }
    }
}
