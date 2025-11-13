using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Controllers.Roles;
using IDV_Backend.Services.AdminLogs;
using IDV_Backend.Services.Roles;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace UserTest.Controllers.Roles
{
    public class RolesPermissionCatalogControllerTests
    {
        [Test]
        public async Task GetPermissionCatalog_Returns_Ok_With_Annotated_Catalog()
        {
            var svc = new Mock<IRoleService>();
            var logger = new Mock<IAdminActionLogger>();

            var sample = new List<PermissionSelectionDto>
            {
                new(1, "ConfigureRbac", "Configure RBAC", null, true),
                new(2, "ViewRespondVerifications", "View/Respond", null, false),
            };
            svc.Setup(s => s.GetPermissionCatalogForRoleAsync(5, It.IsAny<CancellationToken>()))
               .ReturnsAsync(sample);

            var ctrl = new RolesController(svc.Object, logger.Object);

            var res = await ctrl.GetPermissionCatalog(5, CancellationToken.None);
            var ok = res as OkObjectResult;
            Assert.That(ok, Is.Not.Null);
            var value = ok!.Value as IReadOnlyList<PermissionSelectionDto>;
            Assert.That(value, Is.Not.Null);
            Assert.That(value!.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task GetPermissionCatalog_Returns_NotFound_When_Role_Missing()
        {
            var svc = new Mock<IRoleService>();
            var logger = new Mock<IAdminActionLogger>();

            svc.Setup(s => s.GetPermissionCatalogForRoleAsync(42, It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<PermissionSelectionDto>?)null);

            var ctrl = new RolesController(svc.Object, logger.Object);

            var res = await ctrl.GetPermissionCatalog(42, CancellationToken.None);
            Assert.That(res, Is.InstanceOf<NotFoundResult>());
        }
    }
}
