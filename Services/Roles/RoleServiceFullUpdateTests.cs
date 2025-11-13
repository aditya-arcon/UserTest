using FluentValidation;
using FluentValidation.Results;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Models;
using IDV_Backend.Models.Roles;
using IDV_Backend.Repositories.Roles;
using IDV_Backend.Services.Roles;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UserTest.Services.Roles
{
    [TestFixture]
    public class RoleServiceFullUpdateTests
    {
        private Mock<IRoleRepository> _repo = null!;
        private Mock<IPermissionRepository> _perms = null!;
        private Mock<IValidator<CreateRoleRequest>> _createV = null!;
        private Mock<IValidator<UpdateRoleRequest>> _updateV = null!;
        private Mock<IValidator<UpdateRolePermissionsRequest>> _permV = null!;
        private Mock<IRolesVersionService> _rolesVersion = null!;
        private RoleService _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _repo = new Mock<IRoleRepository>(MockBehavior.Strict);
            _perms = new Mock<IPermissionRepository>(MockBehavior.Strict);

            _createV = new Mock<IValidator<CreateRoleRequest>>(MockBehavior.Loose);
            _updateV = new Mock<IValidator<UpdateRoleRequest>>(MockBehavior.Strict);
            _permV = new Mock<IValidator<UpdateRolePermissionsRequest>>(MockBehavior.Strict);

            _rolesVersion = new Mock<IRolesVersionService>(MockBehavior.Strict);

            _updateV
                .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<UpdateRoleRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _permV
                .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<UpdateRolePermissionsRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            // Only BumpAsync is expected by RoleService.UpdateFullAsync; don't set up GetAsync.
            _rolesVersion
                .Setup(s => s.BumpAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0));

            _sut = new RoleService(_repo.Object, _perms.Object, _createV.Object, _updateV.Object, _permV.Object, _rolesVersion.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _repo.VerifyAll();
            _perms.VerifyAll();
            // Do NOT call _rolesVersion.VerifyAll() — each test verifies BumpAsync explicitly.
        }

        [Test]
        public async Task UpdateFullAsync_Succeeds_Atomically()
        {
            const long id = 10;
            var existing = new RoleUserMapping { Id = id, RoleName = RoleName.Admin };
            var req = new UpdateRoleWithPermissionsRequest(nameof(RoleName.SupportAdmin),
                new[] { PermissionCodes.ManageSupportTickets, PermissionCodes.ConfigureRbac });

            _repo.Setup(r => r.GetByIdTrackedAsync(id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(existing);
            _repo.Setup(r => r.ExistsByNameOtherIdAsync(RoleName.SupportAdmin, id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

            _perms.Setup(p => p.GetByCodesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<Permission>
                  {
                      new Permission { Id = 1, Code = PermissionCodes.ManageSupportTickets, Name = "x" },
                      new Permission { Id = 2, Code = PermissionCodes.ConfigureRbac, Name = "y" }
                  });

            _repo.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
                 .Callback<Func<CancellationToken, Task>, CancellationToken>(async (fn, ct) => await fn(ct))
                 .Returns(Task.CompletedTask);

            _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(1);

            _perms.Setup(p => p.ReplaceRolePermissionsAsync(id, It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

            _perms.Setup(p => p.GetCodesByRoleIdAsync(id, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new[] { PermissionCodes.ConfigureRbac, PermissionCodes.ManageSupportTickets });

            var res = await _sut.UpdateFullAsync(id, req);

            Assert.That(res, Is.Not.Null);
            Assert.That(res!.Name, Is.EqualTo(nameof(RoleName.SupportAdmin)));
            CollectionAssert.AreEquivalent(
                new[] { PermissionCodes.ManageSupportTickets, PermissionCodes.ConfigureRbac }, res.Codes);

            // Verify the expected bump occurred exactly once on success
            _rolesVersion.Verify(s => s.BumpAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void UpdateFullAsync_InvalidCode_Throws_BeforeMutation()
        {
            const long id = 10;
            var existing = new RoleUserMapping { Id = id, RoleName = RoleName.Admin };
            var req = new UpdateRoleWithPermissionsRequest(nameof(RoleName.SupportAdmin), new[] { "NOT_A_REAL_CODE" });

            _repo.Setup(r => r.GetByIdTrackedAsync(id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(existing);
            _repo.Setup(r => r.ExistsByNameOtherIdAsync(RoleName.SupportAdmin, id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

            // No permissions resolved -> triggers pre-transaction failure
            _perms.Setup(p => p.GetByCodesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<Permission>());

            Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UpdateFullAsync(id, req));

            // Transaction and save should not be invoked
            _repo.Verify(r => r.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
            _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

            // And bump must not be called on failure
            _rolesVersion.Verify(s => s.BumpAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
