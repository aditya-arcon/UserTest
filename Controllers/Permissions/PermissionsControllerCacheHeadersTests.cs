using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Controllers.Roles;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Models.Roles;
using IDV_Backend.Repositories.Roles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace UserTest.Controllers.Permissions
{
    [TestFixture]
    public class PermissionsControllerCacheHeadersTests
    {
        [Test]
        public async Task GetAll_Sets_NoCache_Headers()
        {
            var repo = new Mock<IPermissionRepository>(MockBehavior.Strict);
            repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Permission>
                {
                    new Permission { Id = 1, Code = "ManageUsersAndRoles", Name = "Manage Users/Roles" }
                });

            var controller = new PermissionsController(repo.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            // Call the action
            var actionResult = await controller.GetAll(CancellationToken.None);

            // Headers should be set on the controller response
            var headers = controller.Response.Headers;
            Assert.That(headers["Cache-Control"].ToString(), Is.EqualTo("no-store, no-cache, must-revalidate"));
            Assert.That(headers["Pragma"].ToString(), Is.EqualTo("no-cache"));
            Assert.That(headers["Expires"].ToString(), Is.EqualTo("0"));

            // The concrete result is OkObjectResult
            var ok = actionResult.Result as OkObjectResult;
            Assert.That(ok, Is.Not.Null);

            // Payload inside OkObjectResult
            var payload = ok!.Value as IReadOnlyList<PermissionDto>;
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.Count, Is.EqualTo(1));
            Assert.That(payload[0].Code, Is.EqualTo("ManageUsersAndRoles"));
        }
    }
}
