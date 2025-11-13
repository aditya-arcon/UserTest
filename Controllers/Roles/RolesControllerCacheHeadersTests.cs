using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Controllers.Roles;
using IDV_Backend.Services.AdminLogs;
using IDV_Backend.Services.Roles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace UserTest.Controllers.Roles
{
    [TestFixture]
    public class RolesControllerCacheHeadersTests
    {
        private Mock<IRoleService> _svc = null!;
        private Mock<IAdminActionLogger> _log = null!;
        private RolesController _controller = null!;

        [SetUp]
        public void SetUp()
        {
            _svc = new Mock<IRoleService>(MockBehavior.Strict);
            _log = new Mock<IAdminActionLogger>(MockBehavior.Loose); // not used on reads

            _controller = new RolesController(_svc.Object, _log.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
        }

        [Test]
        public async Task GetAll_Sets_NoCache_Headers()
        {
            _svc.Setup(s => s.GetAllAsync(false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RoleDto> { new(1, "Admin") });

            var result = await _controller.GetAll(false, CancellationToken.None) as OkObjectResult;
            Assert.That(result, Is.Not.Null);

            var headers = _controller.Response.Headers;
            Assert.That(headers["Cache-Control"].ToString(), Is.EqualTo("no-store, no-cache, must-revalidate"));
            Assert.That(headers["Pragma"].ToString(), Is.EqualTo("no-cache"));
            Assert.That(headers["Expires"].ToString(), Is.EqualTo("0"));
        }

        [Test]
        public async Task GetById_Sets_NoCache_Headers()
        {
            _svc.Setup(s => s.GetPermissionsAsync(5, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RolePermissionsResponse(5, "User", new[] { "ViewRespondVerifications" }));

            var result = await _controller.GetById(5, CancellationToken.None) as OkObjectResult;
            Assert.That(result, Is.Not.Null);

            var headers = _controller.Response.Headers;
            Assert.That(headers["Cache-Control"].ToString(), Is.EqualTo("no-store, no-cache, must-revalidate"));
            Assert.That(headers["Pragma"].ToString(), Is.EqualTo("no-cache"));
            Assert.That(headers["Expires"].ToString(), Is.EqualTo("0"));
        }

        [Test]
        public async Task GetById_NotFound_Does_NotThrow()
        {
            _svc.Setup(s => s.GetPermissionsAsync(99, It.IsAny<CancellationToken>()))
                .ReturnsAsync((RolePermissionsResponse?)null);

            var result = await _controller.GetById(99, CancellationToken.None);
            Assert.That(result, Is.InstanceOf<NotFoundResult>());
        }
    }
}
