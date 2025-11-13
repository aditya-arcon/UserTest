// UserTest/Controllers/RolesControllerAuthAttributesTests.cs
using System.Linq;
using System.Reflection;
using IDV_Backend.Authorization;
using IDV_Backend.Controllers.Roles;
using Microsoft.AspNetCore.Authorization;
using NUnit.Framework;

namespace UserTest.Controllers.Roles
{
    public class RolesControllerAuthAttributesTests
    {
        [Test]
        public void Controller_HasAuthorizeAttribute()
        {
            var attr = typeof(RolesController).GetCustomAttribute<AuthorizeAttribute>();
            Assert.That(attr, Is.Not.Null);
        }

        [Test]
        public void MutatingActions_Use_RequireAdmin_Policy()
        {
            var methods = new[] { "Create", "Update", "Delete" };
            foreach (var name in methods)
            {
                var mi = typeof(RolesController).GetMethod(name);
                Assert.That(mi, Is.Not.Null, $"Missing method: {name}");

                var authAttr = mi!.GetCustomAttributes<AuthorizeAttribute>(true).FirstOrDefault();
                Assert.That(authAttr, Is.Not.Null, $"{name} missing AuthorizeAttribute");
                Assert.That(authAttr!.Policy, Is.EqualTo(Policies.RequireAdmin), $"{name} wrong policy");
            }
        }
    }
}
