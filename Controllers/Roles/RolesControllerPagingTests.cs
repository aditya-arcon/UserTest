using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Contracts.Common;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Controllers.Roles;
using IDV_Backend.Services.AdminLogs;
using IDV_Backend.Services.Roles;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace UserTest.Controllers.Roles
{
    public class RolesControllerPagingTests
    {
        [Test]
        public async Task List_Returns_PagedResult_And_200()
        {
            var svc = new Mock<IRoleService>();
            var logger = new Mock<IAdminActionLogger>();

            var expected = new PagedResult<RoleListItem>
            {
                Items = new List<RoleListItem>
                {
                    new(2, "Admin"),
                    new(1, "SuperAdmin")
                },
                Page = 1,
                PageSize = 2,
                TotalCount = 5
            };

            svc.Setup(s => s.ListAsync(It.IsAny<RoleListQuery>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(expected);

            var ctrl = new RolesController(svc.Object, logger.Object);

            var res = await ctrl.List(new RoleListQuery { Page = 1, PageSize = 2, SortBy = "Name", SortDir = "desc" }, CancellationToken.None)
                      as OkObjectResult;

            Assert.That(res, Is.Not.Null);
            var body = res!.Value as PagedResult<RoleListItem>;
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Items.Count, Is.EqualTo(2));
            Assert.That(body.TotalCount, Is.EqualTo(5));
        }
    }
}
