// UserTest/Authorization/RolesControllerAuthAttributesTests.cs
using System.Linq;
using System.Reflection;
using IDV_Backend.Authorization;
using IDV_Backend.Controllers.Roles;
using IDV_Backend.Models.Roles;
using Microsoft.AspNetCore.Authorization;
using NUnit.Framework;

namespace UserTest.Authorization
{
    [TestFixture]
    public class RolesControllerAuthAttributesTests
    {
        [Test]
        public void Class_Has_Authorize_For_Reads()
        {
            var attr = typeof(RolesController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()
                .FirstOrDefault();

            Assert.That(attr, Is.Not.Null);
            Assert.That(attr!.Policy, Is.Null.Or.Empty); // class-level just requires auth
        }

        [TestCase(nameof(RolesController.Create))]
        [TestCase(nameof(RolesController.Update))]
        [TestCase(nameof(RolesController.Delete))]
        [TestCase(nameof(RolesController.UpdateFull))]
        public void CrudMutations_Require_Admin(string methodName)
        {
            var mi = typeof(RolesController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                            .First(m => m.Name == methodName);

            var auth = mi.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                         .Cast<AuthorizeAttribute>()
                         .FirstOrDefault();

            Assert.That(auth, Is.Not.Null, $"Expected [Authorize] on {methodName}");
            Assert.That(auth!.Policy, Is.EqualTo(Policies.RequireAdmin));
        }

        [TestCase(nameof(RolesController.GetPermissions))]
        [TestCase(nameof(RolesController.SetPermissions))]
        public void PermissionEndpoints_Require_ConfigureRbac(string methodName)
        {
            var mi = typeof(RolesController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                            .First(m => m.Name == methodName);

            var auth = mi.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                         .Cast<AuthorizeAttribute>()
                         .FirstOrDefault();

            Assert.That(auth, Is.Not.Null, $"Expected [Authorize] on {methodName}");
            Assert.That(auth!.Policy, Is.EqualTo($"Perm:{PermissionCodes.ConfigureRbac}"));
        }
    }
}
