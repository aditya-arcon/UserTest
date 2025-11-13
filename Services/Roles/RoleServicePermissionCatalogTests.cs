using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Models;
using IDV_Backend.Models.Roles;
using IDV_Backend.Repositories.Roles;
using IDV_Backend.Services.Roles;
using Moq;
using NUnit.Framework;

namespace UserTest.Services.Roles
{
    public class RoleServicePermissionCatalogTests
    {
        private static RoleUserMapping Role(long id, RoleName name) => new RoleUserMapping { Id = id, RoleName = name };

        [Test]
        public async Task GetPermissionCatalogForRoleAsync_Returns_Annotated_Catalog()
        {
            // Arrange
            var role = Role(2, RoleName.Admin);

            var roleRepo = new Mock<IRoleRepository>();
            roleRepo.Setup(r => r.GetByIdNoTrackingAsync(2, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(role);

            var perms = new[]
            {
                new Permission { Id = 1, Code = PermissionCodes.ConfigureRbac, Name = "Configure RBAC" },
                new Permission { Id = 2, Code = PermissionCodes.ViewRespondVerifs, Name = "View/Respond" },
                new Permission { Id = 3, Code = PermissionCodes.ManageSupportTickets, Name = "Support Tickets" },
            };

            var permRepo = new Mock<IPermissionRepository>();
            permRepo.Setup(p => p.GetAllAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(perms);
            permRepo.Setup(p => p.GetCodesByRoleIdAsync(2, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new[] { PermissionCodes.ViewRespondVerifs });

            var cv = new Mock<IValidator<CreateRoleRequest>>();
            var uv = new Mock<IValidator<UpdateRoleRequest>>();
            var pv = new Mock<IValidator<UpdateRolePermissionsRequest>>();
            var rv = new Mock<IRolesVersionService>();

            var svc = new RoleService(roleRepo.Object, permRepo.Object, cv.Object, uv.Object, pv.Object, rv.Object);

            // Act
            var catalog = await svc.GetPermissionCatalogForRoleAsync(2, CancellationToken.None);

            // Assert
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog!.Count, Is.EqualTo(3));
            var dict = catalog.ToDictionary(x => x.Code, x => x.Selected);
            Assert.That(dict[PermissionCodes.ViewRespondVerifs], Is.True);
            Assert.That(dict[PermissionCodes.ConfigureRbac], Is.False);
            Assert.That(dict[PermissionCodes.ManageSupportTickets], Is.False);
        }

        [Test]
        public async Task GetPermissionCatalogForRoleAsync_Returns_Null_If_Role_NotFound()
        {
            var roleRepo = new Mock<IRoleRepository>();
            roleRepo.Setup(r => r.GetByIdNoTrackingAsync(999, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((RoleUserMapping?)null);

            var permRepo = new Mock<IPermissionRepository>();
            var cv = new Mock<IValidator<CreateRoleRequest>>();
            var uv = new Mock<IValidator<UpdateRoleRequest>>();
            var pv = new Mock<IValidator<UpdateRolePermissionsRequest>>();
            var rv = new Mock<IRolesVersionService>();

            var svc = new RoleService(roleRepo.Object, permRepo.Object, cv.Object, uv.Object, pv.Object, rv.Object);

            var catalog = await svc.GetPermissionCatalogForRoleAsync(999, CancellationToken.None);
            Assert.That(catalog, Is.Null);
        }
    }
}
