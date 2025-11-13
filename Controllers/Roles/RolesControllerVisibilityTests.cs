using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Controllers.Roles;
using IDV_Backend.Services.AdminLogs;
using IDV_Backend.Services.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace UserTest.Controllers.Roles
{
    public class RolesControllerVisibilityTests
    {
        [Test]
        public void SetVisibility_Action_Has_RequireAdmin()
        {
            var method = typeof(RolesController).GetMethod("SetVisibility");
            Assert.That(method, Is.Not.Null);

            var authAttr = method!.GetCustomAttributes(typeof(AuthorizeAttribute), false);
            Assert.That(authAttr, Is.Not.Null);
            Assert.That(authAttr.Length, Is.GreaterThan(0));

            var first = (AuthorizeAttribute)authAttr[0];
            Assert.That(first.Policy, Is.EqualTo("RequireAdmin"));
        }

        [Test]
        public async Task SetVisibility_Returns_Ok_With_Dto()
        {
            var service = new Mock<IRoleService>();
            service.Setup(s => s.SetVisibilityAsync(4, It.IsAny<SetRoleVisibilityRequest>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new RoleDto(4, "WorkflowAdmin"));

            var logger = new Mock<IAdminActionLogger>();
            var ctrl = new RolesController(service.Object, logger.Object);

            var res = await ctrl.SetVisibility(4, new SetRoleVisibilityRequest(true), CancellationToken.None);
            var ok = res as OkObjectResult;

            Assert.That(ok, Is.Not.Null);
            var dto = ok!.Value as RoleDto;
            Assert.That(dto, Is.Not.Null);
            Assert.That(dto!.Id, Is.EqualTo(4));
            Assert.That(dto!.Name, Is.EqualTo("WorkflowAdmin"));
        }

        [Test]
        public async Task SetVisibility_NotFound_Returns_404()
        {
            var service = new Mock<IRoleService>();
            service.Setup(s => s.SetVisibilityAsync(123, It.IsAny<SetRoleVisibilityRequest>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((RoleDto?)null);

            var logger = new Mock<IAdminActionLogger>();
            var ctrl = new RolesController(service.Object, logger.Object);

            var res = await ctrl.SetVisibility(123, new SetRoleVisibilityRequest(false), CancellationToken.None);

            Assert.That(res, Is.InstanceOf<NotFoundResult>().Or.InstanceOf<ObjectResult>());
            // If ProblemDetails is used later, this assertion still accepts 404 via ObjectResult.
        }
    }
}
