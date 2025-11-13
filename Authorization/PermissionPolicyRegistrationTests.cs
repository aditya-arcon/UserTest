using IDV_Backend.Authorization;
using IDV_Backend.Models.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Linq;

namespace UserTest.Authorization
{
    public class PermissionPolicyRegistrationTests
    {
        private ServiceProvider _sp = null!;

        [SetUp]
        public void Setup()
        {
            var services = new ServiceCollection();
            services.AddAuthorization();
            IDV_Backend.Authorization.AuthorizationExtensions.AddAuthorizationPolicies(services);
            _sp = services.BuildServiceProvider();
        }

        [TearDown]
        public void TearDown() => _sp.Dispose();

        [Test]
        public void Role_Convenience_Policies_Are_Registered()
        {
            var provider = _sp.GetRequiredService<IAuthorizationPolicyProvider>();
            Assert.That(provider.GetPolicyAsync(Policies.RequireAdmin).Result, Is.Not.Null, "RequireAdmin missing");
            Assert.That(provider.GetPolicyAsync(Policies.RequireSuperAdmin).Result, Is.Not.Null, "RequireSuperAdmin missing");
            Assert.That(provider.GetPolicyAsync(Policies.AdminOnly).Result, Is.Not.Null, "AdminOnly missing");
            Assert.That(provider.GetPolicyAsync(Policies.SuperAdminOnly).Result, Is.Not.Null, "SuperAdminOnly missing");
            Assert.That(provider.GetPolicyAsync(Policies.AdminOrSuperAdmin).Result, Is.Not.Null, "AdminOrSuperAdmin missing");
        }

        [Test]
        public void VerifiedUser_Policy_Is_Registered()
        {
            var provider = _sp.GetRequiredService<IAuthorizationPolicyProvider>();
            var policy = provider.GetPolicyAsync(Policies.RequireVerifiedUser).Result;
            Assert.That(policy, Is.Not.Null, "RequireVerifiedUser is not registered.");
            // It should contain a ClaimsAuthorizationRequirement for AuthClaimTypes.Verified == "true"
            var claimsReq = policy!.Requirements.OfType<ClaimsAuthorizationRequirement>().FirstOrDefault();
            Assert.That(claimsReq, Is.Not.Null);
            Assert.That(claimsReq!.ClaimType, Is.EqualTo(AuthClaimTypes.Verified));
            CollectionAssert.Contains(claimsReq.AllowedValues!, "true");
        }

        [Test]
        public void Permission_Policies_Are_Registered_With_Correct_ClaimRequirements()
        {
            var provider = _sp.GetRequiredService<IAuthorizationPolicyProvider>();

            string code = PermissionCodes.ManageUsersAndRoles;
            var policy = provider.GetPolicyAsync($"Perm:{code}").Result;
            Assert.That(policy, Is.Not.Null, $"Perm:{code} not registered.");

            var claimReq = policy!.Requirements.OfType<ClaimsAuthorizationRequirement>().FirstOrDefault();
            Assert.That(claimReq, Is.Not.Null, "Permission policy must be a ClaimsAuthorizationRequirement.");
            Assert.That(claimReq!.ClaimType, Is.EqualTo(AuthClaimTypes.Permission));
            CollectionAssert.Contains(claimReq.AllowedValues!, code);
        }
    }
}
