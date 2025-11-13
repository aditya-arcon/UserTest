using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Moq;
using NUnit.Framework;
using IDV_Backend.Models;
using IDV_Backend.Models.Roles;
using IDV_Backend.Services.Roles;
using IDV_Backend.Repositories.Roles;
using IDV_Backend.Contracts.Roles;

namespace UserTest.Roles
{
    public class RoleService_PermissionMatrix_Tests
    {
        private static IValidator<T> PassValidator<T>() => Mock.Of<IValidator<T>>(_ =>
            _.ValidateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()) ==
            Task.FromResult(new FluentValidation.Results.ValidationResult()));

        [Test]
        public async Task Returns_All_Roles_With_Correct_Codes_When_IncludeHidden_True()
        {
            // Arrange
            var roles = new List<RoleUserMapping>
            {
                new() { Id = 1, RoleName = RoleName.SuperAdmin, IsUiHidden = false },
                new() { Id = 2, RoleName = RoleName.Admin,      IsUiHidden = false },
                new() { Id = 3, RoleName = RoleName.SupportAdmin, IsUiHidden = false }, // convention-hidden
                new() { Id = 4, RoleName = RoleName.User,       IsUiHidden = true }     // db-hidden
            };

            var roleRepo = new Mock<IRoleRepository>();
            roleRepo.Setup(r => r.GetAllNoTrackingAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(roles);

            var permRepo = new Mock<IPermissionRepository>();
            permRepo.Setup(p => p.GetCodesByRoleIdAsync(1, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new[] { PermissionCodes.ConfigureRbac, PermissionCodes.EditSystemSettings });
            permRepo.Setup(p => p.GetCodesByRoleIdAsync(2, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new[] { PermissionCodes.ManageUsersAndRoles });
            permRepo.Setup(p => p.GetCodesByRoleIdAsync(3, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new[] { PermissionCodes.ManageSupportTickets });
            permRepo.Setup(p => p.GetCodesByRoleIdAsync(4, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(System.Array.Empty<string>());

            var svc = new RoleService(
                roleRepo.Object,
                permRepo.Object,
                PassValidator<CreateRoleRequest>(),
                PassValidator<UpdateRoleRequest>(),
                PassValidator<UpdateRolePermissionsRequest>(),
                Mock.Of<IRolesVersionService>());

            // Act
            var res = await svc.GetPermissionMatrixAsync(includeHidden: true);

            // Assert
            Assert.That(res, Is.Not.Null);
            Assert.That(res.Items.Count, Is.EqualTo(4));

            var admin = res.Items.Single(i => i.RoleId == 2);
            Assert.That(admin.Name, Is.EqualTo(RoleName.Admin.ToString()));
            Assert.That(admin.Codes, Is.EquivalentTo(new[] { PermissionCodes.ManageUsersAndRoles }));
        }

        [Test]
        public async Task Excludes_Hidden_When_IncludeHidden_False()
        {
            // Arrange
            var roles = new List<RoleUserMapping>
            {
                new() { Id = 1, RoleName = RoleName.SuperAdmin, IsUiHidden = false },
                new() { Id = 2, RoleName = RoleName.SupportAdmin, IsUiHidden = false }, // convention-hidden
                new() { Id = 3, RoleName = RoleName.User, IsUiHidden = true }           // db-hidden
            };

            var roleRepo = new Mock<IRoleRepository>();
            roleRepo.Setup(r => r.GetAllNoTrackingAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(roles);

            var permRepo = new Mock<IPermissionRepository>();
            permRepo.Setup(p => p.GetCodesByRoleIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(System.Array.Empty<string>());

            var svc = new RoleService(
                roleRepo.Object,
                permRepo.Object,
                PassValidator<CreateRoleRequest>(),
                PassValidator<UpdateRoleRequest>(),
                PassValidator<UpdateRolePermissionsRequest>(),
                Mock.Of<IRolesVersionService>());

            // Act
            var res = await svc.GetPermissionMatrixAsync(includeHidden: false);

            // Assert
            Assert.That(res.Items.Select(i => i.RoleId), Is.EquivalentTo(new[] { 1L })); // only SuperAdmin remains
        }
    }
}
