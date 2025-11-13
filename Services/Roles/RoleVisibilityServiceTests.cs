using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Models;
using IDV_Backend.Repositories.Roles;
using IDV_Backend.Services.Roles;
using Moq;
using NUnit.Framework;

namespace UserTest.Services.Roles
{
    public class RoleVisibilityServiceTests
    {
        [Test]
        public async Task SetVisibilityAsync_Toggles_IsUiHidden_And_Bumps_Version_When_Changed()
        {
            var role = new RoleUserMapping { Id = 7, RoleName = RoleName.SupportAdmin, IsUiHidden = true };

            var repo = new Mock<IRoleRepository>();
            repo.Setup(r => r.GetByIdTrackedAsync(7, It.IsAny<CancellationToken>()))
                .ReturnsAsync(role);
            repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            var perms = new Mock<IPermissionRepository>();
            var createV = new Mock<IValidator<CreateRoleRequest>>();
            var updateV = new Mock<IValidator<UpdateRoleRequest>>();
            var permV = new Mock<IValidator<UpdateRolePermissionsRequest>>();
            var rolesVer = new Mock<IRolesVersionService>();

            var svc = new RoleService(repo.Object, perms.Object, createV.Object, updateV.Object, permV.Object, rolesVer.Object);

            var dto = await svc.SetVisibilityAsync(7, new SetRoleVisibilityRequest(IsHidden: false), CancellationToken.None);

            Assert.That(dto, Is.Not.Null);
            Assert.That(role.IsUiHidden, Is.False);
            rolesVer.Verify(v => v.BumpAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task SetVisibilityAsync_NoChange_Does_Not_Bump_Version()
        {
            var role = new RoleUserMapping { Id = 2, RoleName = RoleName.Admin, IsUiHidden = false };

            var repo = new Mock<IRoleRepository>();
            repo.Setup(r => r.GetByIdTrackedAsync(2, It.IsAny<CancellationToken>()))
                .ReturnsAsync(role);
            repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            var perms = new Mock<IPermissionRepository>();
            var createV = new Mock<IValidator<CreateRoleRequest>>();
            var updateV = new Mock<IValidator<UpdateRoleRequest>>();
            var permV = new Mock<IValidator<UpdateRolePermissionsRequest>>();
            var rolesVer = new Mock<IRolesVersionService>();

            var svc = new RoleService(repo.Object, perms.Object, createV.Object, updateV.Object, permV.Object, rolesVer.Object);

            var dto = await svc.SetVisibilityAsync(2, new SetRoleVisibilityRequest(IsHidden: false), CancellationToken.None);

            Assert.That(dto, Is.Not.Null);
            Assert.That(role.IsUiHidden, Is.False);
            rolesVer.Verify(v => v.BumpAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task SetVisibilityAsync_NotFound_Returns_Null()
        {
            var repo = new Mock<IRoleRepository>();
            repo.Setup(r => r.GetByIdTrackedAsync(999, It.IsAny<CancellationToken>()))
                .ReturnsAsync((RoleUserMapping?)null);

            var perms = new Mock<IPermissionRepository>();
            var createV = new Mock<IValidator<CreateRoleRequest>>();
            var updateV = new Mock<IValidator<UpdateRoleRequest>>();
            var permV = new Mock<IValidator<UpdateRolePermissionsRequest>>();
            var rolesVer = new Mock<IRolesVersionService>();

            var svc = new RoleService(repo.Object, perms.Object, createV.Object, updateV.Object, permV.Object, rolesVer.Object);

            var dto = await svc.SetVisibilityAsync(999, new SetRoleVisibilityRequest(IsHidden: true), CancellationToken.None);

            Assert.That(dto, Is.Null);
            rolesVer.Verify(v => v.BumpAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
