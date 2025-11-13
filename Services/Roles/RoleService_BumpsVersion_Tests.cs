using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Models;
using IDV_Backend.Models.Roles;
using IDV_Backend.Repositories.Roles;
using IDV_Backend.Services.Roles;
using FluentValidation;
using Moq;
using NUnit.Framework;

namespace UserTest.Services.Roles
{
    [TestFixture]
    public class RoleService_BumpsVersion_Tests
    {
        [Test]
        public async Task SetPermissions_Bumps_Version()
        {
            var repo = new Mock<IRoleRepository>();
            repo.Setup(r => r.GetByIdTrackedAsync(2, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RoleUserMapping { Id = 2, RoleName = RoleName.Admin });
            repo.Setup(r => r.GetByIdNoTrackingAsync(2, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RoleUserMapping { Id = 2, RoleName = RoleName.Admin });

            var permsRepo = new Mock<IPermissionRepository>();
            permsRepo.Setup(p => p.GetByCodesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<Permission> { new Permission { Id = 3, Code = PermissionCodes.ConfigureRbac } });
            permsRepo.Setup(p => p.ReplaceRolePermissionsAsync(2, It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
            permsRepo.Setup(p => p.GetCodesByRoleIdAsync(2, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<string> { PermissionCodes.ConfigureRbac });

            var createV = new Mock<IValidator<CreateRoleRequest>>();
            createV.Setup(v => v.ValidateAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new FluentValidation.Results.ValidationResult());

            var updateV = new Mock<IValidator<UpdateRoleRequest>>();
            updateV.Setup(v => v.ValidateAsync(It.IsAny<UpdateRoleRequest>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new FluentValidation.Results.ValidationResult());

            var permsV = new Mock<IValidator<UpdateRolePermissionsRequest>>();
            permsV.Setup(v => v.ValidateAsync(It.IsAny<UpdateRolePermissionsRequest>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FluentValidation.Results.ValidationResult());

            var rv = new Mock<IRolesVersionService>(MockBehavior.Strict);
            rv.Setup(s => s.BumpAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

            var service = new RoleService(repo.Object, permsRepo.Object, createV.Object, updateV.Object, permsV.Object, rv.Object);

            var resp = await service.SetPermissionsAsync(2, new UpdateRolePermissionsRequest(new[] { PermissionCodes.ConfigureRbac }), CancellationToken.None);

            Assert.That(resp.Codes, Is.EquivalentTo(new[] { PermissionCodes.ConfigureRbac }));
            rv.Verify(s => s.BumpAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
