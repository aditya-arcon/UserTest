using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using IDV_Backend.Contracts.Common;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Models;
using IDV_Backend.Repositories.Roles;
using IDV_Backend.Services.Roles;
using Moq;
using NUnit.Framework;

namespace UserTest.Services.Roles
{
    public class RoleServicePagingTests
    {
        private static RoleUserMapping R(long id, RoleName rn) => new RoleUserMapping { Id = id, RoleName = rn };

        private (RoleService svc, Mock<IRoleRepository> repo) MakeService(IEnumerable<RoleUserMapping> roles)
        {
            var repo = new Mock<IRoleRepository>();
            repo.Setup(r => r.GetAllNoTrackingAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(roles.ToList());

            // unused paths here:
            var perms = new Mock<IPermissionRepository>();
            var createV = new Mock<IValidator<CreateRoleRequest>>();
            var updateV = new Mock<IValidator<UpdateRoleRequest>>();
            var permV = new Mock<IValidator<UpdateRolePermissionsRequest>>();
            var rolesVer = new Mock<IRolesVersionService>();

            var svc = new RoleService(repo.Object, perms.Object, createV.Object, updateV.Object, permV.Object, rolesVer.Object);
            return (svc, repo);
        }

        [Test]
        public async Task ListAsync_Default_SortsByName_Asc_And_Hides_SupportAdmin()
        {
            var roles = new[]
            {
                R(1, RoleName.SuperAdmin),
                R(2, RoleName.Admin),
                R(3, RoleName.User),
                R(7, RoleName.SupportAdmin),
                R(4, RoleName.WorkflowAdmin)
            };

            var (svc, _) = MakeService(roles);

            var page = await svc.ListAsync(new RoleListQuery { Page = 1, PageSize = 10 }, CancellationToken.None);

            Assert.That(page.TotalCount, Is.EqualTo(4)); // SupportAdmin hidden
            Assert.That(page.Items.Select(i => i.Name).ToArray(),
                Is.EqualTo(new[] { "Admin", "SuperAdmin", "User", "WorkflowAdmin" }.OrderBy(x => x).ToArray()));
        }

        [Test]
        public async Task ListAsync_IncludeHidden_True_Returns_All()
        {
            var roles = new[]
            {
                R(1, RoleName.SuperAdmin),
                R(2, RoleName.Admin),
                R(3, RoleName.User),
                R(7, RoleName.SupportAdmin),
                R(4, RoleName.WorkflowAdmin)
            };

            var (svc, _) = MakeService(roles);

            var page = await svc.ListAsync(new RoleListQuery { IncludeHidden = true, Page = 1, PageSize = 50, SortBy = "Id", SortDir = "asc" }, CancellationToken.None);

            Assert.That(page.TotalCount, Is.EqualTo(5));
            Assert.That(page.Items.Select(i => i.Id), Is.EqualTo(new long[] { 1, 2, 3, 4, 7 }));
        }

        [Test]
        public async Task ListAsync_Search_Filters_CaseInsensitive()
        {
            var roles = new[]
            {
                R(1, RoleName.SuperAdmin),
                R(2, RoleName.Admin),
                R(3, RoleName.User),
                R(4, RoleName.WorkflowAdmin),
                R(7, RoleName.SupportAdmin)
            };

            var (svc, _) = MakeService(roles);

            var page = await svc.ListAsync(new RoleListQuery { Search = "admin", IncludeHidden = true, Page = 1, PageSize = 10 }, CancellationToken.None);

            var names = page.Items.Select(i => i.Name).ToArray();
            Assert.That(names, Is.EquivalentTo(new[] { "Admin", "SuperAdmin", "WorkflowAdmin", "SupportAdmin" }));
        }

        [Test]
        public async Task ListAsync_Paging_Works_As_Expected()
        {
            var roles = new[]
            {
                R(1, RoleName.SuperAdmin),
                R(2, RoleName.Admin),
                R(3, RoleName.User),
                R(4, RoleName.WorkflowAdmin),
                R(8, RoleName.ReadOnlyAuditor),
                R(6, RoleName.VerificationAgent),
            };

            var (svc, _) = MakeService(roles);

            var page1 = await svc.ListAsync(new RoleListQuery { Page = 1, PageSize = 2, SortBy = "Name", SortDir = "asc", IncludeHidden = true }, CancellationToken.None);
            var page2 = await svc.ListAsync(new RoleListQuery { Page = 2, PageSize = 2, SortBy = "Name", SortDir = "asc", IncludeHidden = true }, CancellationToken.None);

            Assert.That(page1.Items.Count, Is.EqualTo(2));
            Assert.That(page1.HasNext, Is.True);
            Assert.That(page1.HasPrevious, Is.False);

            Assert.That(page2.Items.Count, Is.EqualTo(2));
            Assert.That(page2.HasNext, Is.True);
            Assert.That(page2.HasPrevious, Is.True);
        }
    }
}
