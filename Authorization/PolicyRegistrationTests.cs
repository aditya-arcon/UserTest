// UserTest/Authorization/PolicyRegistrationTests.cs
using IDV_Backend.Authorization;
// IMPORTANT: do NOT import IDV_Backend.Extensions to avoid ambiguity
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace UserTest.Authorization
{
    public class PolicyRegistrationTests
    {
        [Test]
        public void Policies_AreRegistered()
        {
            var services = new ServiceCollection();

            // Core auth services FIRST
            services.AddAuthorization();

            // Call the extension that production code uses
            IDV_Backend.Extensions.AuthorizationExtensions.AddAuthorizationPolicies(services);

            var sp = services.BuildServiceProvider();
            var opts = sp.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

            Assert.That(opts.GetPolicy(Policies.RequireAdmin), Is.Not.Null);
            Assert.That(opts.GetPolicy(Policies.RequireSuperAdmin), Is.Not.Null);
            Assert.That(opts.GetPolicy(Policies.RequireVerifiedUser), Is.Not.Null);
        }
    }
}
