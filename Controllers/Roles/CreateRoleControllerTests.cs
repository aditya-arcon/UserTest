using IDV_Backend.Contracts.Roles;
using IDV_Backend.Contracts.Roles.Validators;
using IDV_Backend.Controllers.Roles;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.AdminLogs;
using IDV_Backend.Models.Roles;
using IDV_Backend.Repositories.Roles;
using IDV_Backend.Services.AdminLogs;
using IDV_Backend.Services.Roles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UserTest.Controllers.Roles
{
    [TestFixture]
    public class CreateRoleControllerTests
    {
        private ApplicationDbContext _db = null!;
        private IPermissionRepository _permRepo = null!;
        private IRoleRepository _roleRepo = null!;
        private IRoleService _svc = null!;
        private RolesController _controller = null!;

        [SetUp]
        public void SetUp()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
                .Options;

            _db = new ApplicationDbContext(opts);

            // Seed permission catalog
            var all = new[]
            {
        new Permission { Code = PermissionCodes.ManageUsersAndRoles, Name = "Manage Users/Roles" },
        new Permission { Code = PermissionCodes.CreateEditWorkflows, Name = "Create/Edit Workflows" },
        new Permission { Code = PermissionCodes.ConfigureRbac, Name = "Configure RBAC" },
        new Permission { Code = PermissionCodes.ViewRespondVerifs, Name = "View/Respond Verifications" },
        new Permission { Code = PermissionCodes.AccessSensitiveData, Name = "Access Sensitive Data" },
        new Permission { Code = PermissionCodes.ManualOverrideReview, Name = "Manual Override Review" },
        new Permission { Code = PermissionCodes.ApiIntegrationMgmt, Name = "API Integration Mgmt" },
        new Permission { Code = PermissionCodes.EditSystemSettings, Name = "Edit System Settings" },
        new Permission { Code = PermissionCodes.ManageSupportTickets, Name = "Manage Support Tickets" },
    };
            _db.Permissions.AddRange(all);
            _db.SaveChanges();

            _permRepo = new PermissionRepository(_db);
            _roleRepo = new RoleRepository(_db);

            var createValidator = new CreateRoleRequestValidator();
            var updateValidator = new UpdateRoleRequestValidator();
            var permValidator = new UpdateRolePermissionsRequestValidator();

            // Strict mock that now allows BumpAsync
            var mockRolesVersion = new Mock<IRolesVersionService>(MockBehavior.Strict);
            mockRolesVersion
                .Setup(s => s.BumpAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            _svc = new RoleService(_roleRepo, _permRepo, createValidator, updateValidator, permValidator, mockRolesVersion.Object);

            var mockLogger = new Mock<IAdminActionLogger>(MockBehavior.Strict);
            mockLogger
                .Setup(l => l.LogSuccessAsync(
                    It.IsAny<AdminAction>(),
                    It.IsAny<string?>(),
                    It.IsAny<long?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);

            mockLogger
                .Setup(l => l.LogFailureAsync(
                    It.IsAny<AdminAction>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<long?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);

            _controller = new RolesController(_svc, mockLogger.Object);
        }


        [TearDown] public void TearDown() => _db.Dispose();

        [Test]
        public async Task CreateRole_WithValidNameAndCodes_Returns201_AndPersists()
        {
            var req = new CreateRoleRequest(
                "SupportAdmin",
                new[] { PermissionCodes.ManageSupportTickets, PermissionCodes.ViewRespondVerifs });

            var result = await _controller.Create(req, CancellationToken.None) as CreatedAtActionResult;
            Assert.That(result, Is.Not.Null, "Expected CreatedAtActionResult");

            var response = result!.Value as RolePermissionsResponse;
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Name, Is.EqualTo("SupportAdmin"));
            CollectionAssert.AreEquivalent(
                new[] { PermissionCodes.ManageSupportTickets, PermissionCodes.ViewRespondVerifs },
                response.Codes);

            // DB check
            var role = _db.Roles.Single(r => r.Id == response.RoleId);
            Assert.That(role.RoleName, Is.EqualTo(RoleName.SupportAdmin));

            var mappedCodes = _db.RolePermissions.Where(rp => rp.RoleId == role.Id)
                .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (_, p) => p.Code)
                .ToArray();

            CollectionAssert.AreEquivalent(response.Codes, mappedCodes);
        }

        [Test]
        public async Task CreateRole_DuplicateName_Returns400()
        {
            // Seed role
            _db.Roles.Add(new RoleUserMapping { RoleName = RoleName.VerificationAgent });
            _db.SaveChanges();

            var req = new CreateRoleRequest("VerificationAgent", new[] { PermissionCodes.ViewRespondVerifs });

            var result = await _controller.Create(req, CancellationToken.None);
            var problem = result as ObjectResult;

            Assert.That(problem, Is.Not.Null);
            Assert.That(problem!.StatusCode, Is.EqualTo(400));
        }

        [Test]
        public async Task CreateRole_WithInvalidPermissionCode_IsAtomic_NoRoleCreated()
        {
            var name = "ComplianceOfficer";
            var req = new CreateRoleRequest(name, new[] { PermissionCodes.AccessSensitiveData, "NotARealCode" });

            var result = await _controller.Create(req, CancellationToken.None);
            var problem = result as ObjectResult;
            Assert.That(problem, Is.Not.Null);
            Assert.That(problem!.StatusCode, Is.EqualTo(400), "Invalid codes should produce 400.");

            // Ensure NO role added
            Assert.That(_db.Roles.Any(r => r.RoleName.ToString() == name), Is.False);
        }
    }
}
