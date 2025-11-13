using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Authorization;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Controllers.Roles;
using IDV_Backend.Models.Roles;
using IDV_Backend.Services.AdminLogs;
using IDV_Backend.Services.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace UserTest.Roles
{
    public class RolesController_PermissionMatrix_Tests
    {
        [Test]
        public void Has_Expected_Authorize_Policy()
        {
            var method = typeof(RolesController).GetMethod("GetPermissionMatrix");
            Assert.That(method, Is.Not.Null, "GetPermissionMatrix action not found.");

            if (method == null)
                Assert.Fail("GetPermissionMatrix action not found.");

            var attr = method?.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                             .Cast<AuthorizeAttribute>()
                             .FirstOrDefault();

            Assert.That(attr, Is.Not.Null, "AuthorizeAttribute missing on GetPermissionMatrix.");
            Assert.That(attr!.Policy, Is.EqualTo($"Perm:{PermissionCodes.ConfigureRbac}"));
        }

        [Test]
        public async Task Returns_200_And_NoCache_Headers()
        {
            // Arrange
            var svc = new Mock<IRoleService>();
            svc.Setup(s => s.GetPermissionMatrixAsync(true, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new RolePermissionMatrixResponse(new[]
               {
                   new RolePermissionMatrixItem(1, "Admin", new[] { PermissionCodes.ManageUsersAndRoles })
               }));

            var logger = new Mock<IAdminActionLogger>();
            var controller = new RolesController(svc.Object, logger.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            // Act
            var result = await controller.GetPermissionMatrix(includeHidden: true, CancellationToken.None);

            // Assert
            var ok = result as OkObjectResult;
            Assert.That(ok, Is.Not.Null);
            Assert.That(ok?.StatusCode, Is.EqualTo(StatusCodes.Status200OK));

            var payload = ok?.Value as RolePermissionMatrixResponse;
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.Items.Count, Is.EqualTo(1));

            var headers = controller.HttpContext.Response.Headers;
            Assert.That(headers["Cache-Control"].ToString(), Does.Contain("no-store"));
            Assert.That(headers["Pragma"].ToString(), Does.Contain("no-cache"));
            Assert.That(headers["Expires"].ToString(), Is.EqualTo("0"));
        }
    }
}
