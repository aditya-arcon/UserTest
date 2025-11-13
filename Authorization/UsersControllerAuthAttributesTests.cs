using System.Linq;
using System.Reflection;
using IDV_Backend.Controllers.Users;
using IDV_Backend.Models.Roles;
using Microsoft.AspNetCore.Authorization;
using NUnit.Framework;

namespace UserTest.Authorization
{
    [TestFixture]
    public class UsersControllerAuthAttributesTests
    {
        [Test]
        public void Class_Has_Authorize()
        {
            var attr = typeof(UsersController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()
                .FirstOrDefault();

            Assert.That(attr, Is.Not.Null);
        }

        [TestCase(nameof(UsersController.GetAll))]
        [TestCase(nameof(UsersController.GetById))]
        [TestCase(nameof(UsersController.Create))]
        [TestCase(nameof(UsersController.Update))]
        [TestCase(nameof(UsersController.Delete))]
        [TestCase(nameof(UsersController.Deprovision))]
        [TestCase(nameof(UsersController.Reprovision))]
        public void All_Admin_User_Operations_Require_ManageUsersAndRoles(string methodName)
        {
            var mi = typeof(UsersController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                            .First(m => m.Name == methodName);

            var auth = mi.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                         .Cast<AuthorizeAttribute>()
                         .FirstOrDefault();

            Assert.That(auth, Is.Not.Null, $"Expected [Authorize] on {methodName}");
            Assert.That(auth!.Policy, Is.EqualTo($"Perm:{PermissionCodes.ManageUsersAndRoles}"));
        }
    }
}
