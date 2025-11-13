// UserTest/Security/AuthorizationPoliciesTests.cs
using IDV_Backend.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Linq;

namespace UserTest.Security
{
    [TestFixture]
    public class AuthorizationPoliciesTests
    {
        private ServiceProvider _sp = default!;

        [SetUp]
        public void SetUp()
        {
            var services = new ServiceCollection();
            services.AddAuthorization();         // core services
            services.AddAuthorizationPolicies(); // our extension
            _sp = services.BuildServiceProvider();
        }

        [TearDown]
        public void TearDown() => _sp.Dispose();

        [TestCase("SuperAdminOnly", new[] { "SuperAdmin" })]
        [TestCase("AdminOrSuperAdmin", new[] { "Admin", "SuperAdmin" })]
        [TestCase("AdminOnly", new[] { "Admin" })]
        public void Policy_IsRegistered_WithExpectedRoles(string policyName, string[] roles)
        {
            var opts = _sp.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
            Assert.That(opts.GetPolicy(policyName), Is.Not.Null, $"Policy '{policyName}' not registered.");

            var policy = opts.GetPolicy(policyName)!;
            var roleReq = policy.Requirements.OfType<RolesAuthorizationRequirement>().FirstOrDefault();
            Assert.That(roleReq, Is.Not.Null, $"Policy '{policyName}' does not contain a RolesAuthorizationRequirement.");

            var allowedRoles = roleReq!.AllowedRoles.ToArray();
            CollectionAssert.AreEquivalent(roles, allowedRoles, $"Policy '{policyName}' roles mismatch.");
        }
    }
}
