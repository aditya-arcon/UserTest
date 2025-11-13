// UserTest/Authorization/AuthorizationPoliciesRegistrationTests.cs
using IDV_Backend.Authorization;
using IDV_Backend.Authorization.Requirements;
// IMPORTANT: do NOT import IDV_Backend.Extensions to avoid ambiguity
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Linq;

namespace UserTest.Authorization
{
    public class AuthorizationPoliciesRegistrationTests
    {
        private ServiceProvider _sp = null!;

        [SetUp]
        public void Setup()
        {
            var services = new ServiceCollection();

            // Core auth services FIRST
            services.AddAuthorization();

            // Call the extension that production code uses (the one that actually registers your policies)
            IDV_Backend.Extensions.AuthorizationExtensions.AddAuthorizationPolicies(services);

            _sp = services.BuildServiceProvider();
        }

        [TearDown] public void TearDown() => _sp.Dispose();

        [Test]
        public void RequireAdminPolicy_IsRegistered_And_Allows_Admin_Or_SuperAdmin()
        {
            var provider = _sp.GetRequiredService<IAuthorizationPolicyProvider>();
            var policy = provider.GetPolicyAsync(Policies.RequireAdmin).GetAwaiter().GetResult();
            Assert.That(policy, Is.Not.Null, "RequireAdmin policy not registered.");
            var roles = policy!.Requirements.OfType<RolesAuthorizationRequirement>().FirstOrDefault();
            Assert.That(roles, Is.Not.Null);
            CollectionAssert.IsSupersetOf(roles!.AllowedRoles!, new[] { "Admin", "SuperAdmin" });
        }

        [Test]
        public void RequireSuperAdminPolicy_IsRegistered_And_Allows_Only_SuperAdmin()
        {
            var provider = _sp.GetRequiredService<IAuthorizationPolicyProvider>();
            var policy = provider.GetPolicyAsync(Policies.RequireSuperAdmin).GetAwaiter().GetResult();
            Assert.That(policy, Is.Not.Null, "RequireSuperAdmin policy not registered.");
            var roles = policy!.Requirements.OfType<RolesAuthorizationRequirement>().FirstOrDefault();
            Assert.That(roles, Is.Not.Null);
            CollectionAssert.AreEquivalent(new[] { "SuperAdmin" }, roles!.AllowedRoles);
        }

        [Test]
        public void RequireVerifiedUserPolicy_IsRegistered_And_Has_VerifiedUserRequirement()
        {
            var provider = _sp.GetRequiredService<IAuthorizationPolicyProvider>();
            var policy = provider.GetPolicyAsync(Policies.RequireVerifiedUser).GetAwaiter().GetResult();
            Assert.That(policy, Is.Not.Null, "RequireVerifiedUser policy not registered.");
            Assert.That(policy!.Requirements.Any(r => r is VerifiedUserRequirement), Is.True);
        }
    }
}
