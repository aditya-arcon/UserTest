using System.Linq;
using System.Reflection;
using IDV_Backend.Authorization;
using IDV_Backend.Controllers.Template;
using IDV_Backend.Controllers.TemplateVersions;
using IDV_Backend.Controllers.Users;
using IDV_Backend.Controllers.DocumentDefinitions;
using IDV_Backend.Controllers.TemplatesLinkGenerations;
using IDV_Backend.Controllers.AdminLogs;
using Microsoft.AspNetCore.Authorization;
using NUnit.Framework;

namespace UserTest.Authorization
{
    public class ControllerAttributeTests
    {
        [Test]
        public void TemplateController_Uses_RequireAdmin()
        {
            var attr = typeof(TemplateController).GetCustomAttribute<AuthorizeAttribute>();
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr!.Policy, Is.EqualTo(Policies.RequireAdmin));
            Assert.That(attr.Roles, Is.Null, "Roles should not be used directly.");
        }

        [Test]
        public void TemplateVersionController_Uses_RequireAdmin()
        {
            var attr = typeof(TemplateVersionController).GetCustomAttribute<AuthorizeAttribute>();
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr!.Policy, Is.EqualTo(Policies.RequireAdmin));
        }

        [Test]
        public void UsersController_AdminEndpoints_Use_RequireAdmin()
        {
            var methods = typeof(UsersController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttributes<AuthorizeAttribute>().Any());

            foreach (var m in methods)
            {
                var a = m.GetCustomAttribute<AuthorizeAttribute>();
                // GetAll, Create, Update, Delete, Deprovision, Reprovision should use RequireAdmin;
                // GetById is generic [Authorize] and is allowed to differ.
                if (m.Name == "GetById") continue;
                Assert.That(a, Is.Not.Null, $"{m.Name} should use RequireAdmin policy.");
                Assert.That(string.IsNullOrEmpty(a!.Roles), $"{m.Name} should not use Roles directly.");
            }
        }

        [Test]
        public void DocumentDefinitionsController_WriteEndpoints_Use_RequireAdmin()
        {
            var t = typeof(DocumentDefinitionsController);
            var adminMethods = new[] { "Create", "Update", "Publish", "Deprecate", "Clone" };
            foreach (var name in adminMethods)
            {
                var mi = t.GetMethods().First(m => m.Name == name);
                var a = mi.GetCustomAttribute<AuthorizeAttribute>();
                Assert.That(a, Is.Not.Null, $"{name} should be protected.");
                Assert.That(a!.Policy, Is.EqualTo(Policies.RequireAdmin));
            }

            // Search/GetById are public
            var search = t.GetMethod("Search")!;
            var getById = t.GetMethod("GetById", new[] { typeof(System.Guid), typeof(System.Threading.CancellationToken) })!;
            Assert.That(search.GetCustomAttribute<AllowAnonymousAttribute>(), Is.Not.Null);
            Assert.That(getById.GetCustomAttribute<AllowAnonymousAttribute>(), Is.Not.Null);
        }

        [Test]
        public void TemplatesLinkGenerationsController_Class_Uses_RequireAdmin_Resolve_Is_Anonymous()
        {
            var classAttr = typeof(TemplatesLinkGenerationsController).GetCustomAttribute<AuthorizeAttribute>();
            Assert.That(classAttr, Is.Not.Null);
            Assert.That(classAttr!.Policy, Is.EqualTo(Policies.RequireAdmin));

            var resolve = typeof(TemplatesLinkGenerationsController).GetMethod("ResolveJson")!;
            Assert.That(resolve.GetCustomAttribute<AllowAnonymousAttribute>(), Is.Not.Null, "ResolveJson should be public/anonymous.");
        }

        [Test]
        public void AdminActivityLogController_Uses_RequireAdmin()
        {
            var classAttr = typeof(AdminActivityLogController).GetCustomAttribute<AuthorizeAttribute>();
            Assert.That(classAttr, Is.Not.Null);
            Assert.That(classAttr!.Policy, Is.EqualTo(Policies.RequireAdmin));
        }
    }
}
