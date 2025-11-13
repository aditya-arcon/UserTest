using IDV_Backend.Authorization;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Controllers.Roles;
using IDV_Backend.Models.AdminLogs;
using IDV_Backend.Services.AdminLogs;
using IDV_Backend.Services.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UserTest.Controllers.Roles
{
    [TestFixture]
    public class UpdateFullRoleControllerTests
    {
        [Test]
        public async Task UpdateFull_Success_Returns200_With_Permissions()
        {
            var svc = new Mock<IRoleService>(MockBehavior.Strict);
            var logger = new Mock<IAdminActionLogger>(MockBehavior.Strict);

            var req = new UpdateRoleWithPermissionsRequest("SupportAdmin", new[] { "ConfigureRbac" });
            var payload = new RolePermissionsResponse(42, "SupportAdmin", new[] { "ConfigureRbac" });

            svc.Setup(s => s.UpdateFullAsync(42, req, It.IsAny<CancellationToken>()))
               .ReturnsAsync(payload);

            logger.Setup(l => l.LogSuccessAsync(
                AdminAction.RoleUpdate,
                It.IsAny<string?>(),
                42,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);

            var controller = new RolesController(svc.Object, logger.Object);

            var result = await controller.UpdateFull(42, req, CancellationToken.None) as OkObjectResult;
            Assert.That(result, Is.Not.Null);

            var body = result!.Value as RolePermissionsResponse;
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Name, Is.EqualTo("SupportAdmin"));
            CollectionAssert.AreEquivalent(new[] { "ConfigureRbac" }, body.Codes);
        }

        [Test]
        public async Task UpdateFull_NotFound_Returns404()
        {
            var svc = new Mock<IRoleService>(MockBehavior.Strict);
            var logger = new Mock<IAdminActionLogger>(MockBehavior.Strict);

            var req = new UpdateRoleWithPermissionsRequest("SupportAdmin", new[] { "ConfigureRbac" });
            svc.Setup(s => s.UpdateFullAsync(999, req, It.IsAny<CancellationToken>()))
               .ReturnsAsync((RolePermissionsResponse?)null);

            logger.Setup(l => l.LogFailureAsync(
                AdminAction.RoleUpdate,
                "Role not found",
                It.IsAny<string?>(),
                999,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);

            var controller = new RolesController(svc.Object, logger.Object);
            var result = await controller.UpdateFull(999, req, CancellationToken.None);
            Assert.That(result, Is.InstanceOf<NotFoundResult>());
        }

        [Test]
        public void UpdateFull_Invalid_Returns400_Problem()
        {
            var svc = new Mock<IRoleService>(MockBehavior.Strict);
            var logger = new Mock<IAdminActionLogger>(MockBehavior.Strict);

            var req = new UpdateRoleWithPermissionsRequest("SupportAdmin", new[] { "NOT_A_REAL_CODE" });

            svc.Setup(s => s.UpdateFullAsync(42, req, It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("One or more permissions do not exist."));

            logger.Setup(l => l.LogFailureAsync(
                AdminAction.RoleUpdate,
                It.Is<string>(m => m.StartsWith("One or more permissions")),
                It.IsAny<string?>(),
                42,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);

            var controller = new RolesController(svc.Object, logger.Object);

            var result = controller.UpdateFull(42, req, CancellationToken.None).GetAwaiter().GetResult();
            var problem = result as ObjectResult;

            Assert.That(problem, Is.Not.Null);
            Assert.That(problem!.StatusCode, Is.EqualTo(400));
        }

        [Test]
        public void UpdateFull_Has_RequireAdmin_AuthorizeAttribute()
        {
            var mi = typeof(RolesController).GetMethod(nameof(RolesController.UpdateFull));
            Assert.That(mi, Is.Not.Null);

            var attr = mi!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).FirstOrDefault() as AuthorizeAttribute;
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr!.Policy, Is.EqualTo(Policies.RequireAdmin));
        }
    }
}
