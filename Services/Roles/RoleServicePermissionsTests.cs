//using FluentValidation;
//using FluentValidation.Results;
//using IDV_Backend.Contracts.Roles;
//using IDV_Backend.Models;
//using IDV_Backend.Models.Roles;
//using IDV_Backend.Repositories.Roles;
//using IDV_Backend.Services.Roles;
//using Moq;
//using NUnit.Framework;
//using NUnit.Framework.Legacy;

//namespace UserTest.Services.Roles;

//[TestFixture]
//public class RoleServicePermissionsTests
//{
//    private Mock<IRoleRepository> _roles = null!;
//    private Mock<IPermissionRepository> _perms = null!;
//    private IValidator<CreateRoleRequest> _noopCreate = null!;
//    private IValidator<UpdateRoleRequest> _noopUpdate = null!;
//    private IValidator<UpdateRolePermissionsRequest> _permValidator = null!;
//    private RoleService _sut = null!;

//    [SetUp]
//    public void SetUp()
//    {
//        _roles = new Mock<IRoleRepository>(MockBehavior.Strict);
//        _perms = new Mock<IPermissionRepository>(MockBehavior.Strict);

//        _noopCreate = Mock.Of<IValidator<CreateRoleRequest>>(
//            v => v.ValidateAsync(It.IsAny<ValidationContext<CreateRoleRequest>>(), It.IsAny<CancellationToken>())
//                    == Task.FromResult(new ValidationResult()));
//        _noopUpdate = Mock.Of<IValidator<UpdateRoleRequest>>(
//            v => v.ValidateAsync(It.IsAny<ValidationContext<UpdateRoleRequest>>(), It.IsAny<CancellationToken>())
//                    == Task.FromResult(new ValidationResult()));

//        var realPermValidator = new IDV_Backend.Contracts.Roles.Validators.UpdateRolePermissionsRequestValidator();
//        _permValidator = realPermValidator;

//        _sut = new RoleService(_roles.Object, _perms.Object, _noopCreate, _noopUpdate, _permValidator);
//    }

//    [Test]
//    public async Task GetPermissionsAsync_Returns_Null_When_RoleMissing()
//    {
//        _roles.Setup(r => r.GetByIdNoTrackingAsync(123, It.IsAny<CancellationToken>()))
//              .ReturnsAsync((RoleUserMapping?)null);

//        var res = await _sut.GetPermissionsAsync(123);
//        Assert.That(res, Is.Null);
//    }

//    [Test]
//    public async Task GetPermissionsAsync_Returns_Names()
//    {
//        _roles.Setup(r => r.GetByIdNoTrackingAsync(4, It.IsAny<CancellationToken>()))
//              .ReturnsAsync(new RoleUserMapping { Id = 4, RoleName = RoleName.WorkflowAdmin });

//        _perms.Setup(p => p.GetNamesByRoleIdAsync(4, It.IsAny<CancellationToken>()))
//              .ReturnsAsync(new[] {
//                  PermissionNames.CreateEditWorkflows,
//                  PermissionNames.ViewRespondVerifications
//              });

//        var res = await _sut.GetPermissionsAsync(4);
//        Assert.That(res, Is.Not.Null);
//        CollectionAssert.AreEquivalent(
//            new[] { PermissionNames.CreateEditWorkflows, PermissionNames.ViewRespondVerifications },
//            res!.Permissions);
//    }

//    [Test]
//    public void SetPermissionsAsync_Throws_On_Invalid_RoleId()
//    {
//        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
//            _sut.SetPermissionsAsync(0, new UpdateRolePermissionsRequest(new[] { PermissionNames.CreateEditWorkflows })));
//    }

//    [Test]
//    public void SetPermissionsAsync_Throws_When_Role_NotFound()
//    {
//        _roles.Setup(r => r.GetByIdTrackedAsync(9, It.IsAny<CancellationToken>()))
//              .ReturnsAsync((RoleUserMapping?)null);

//        Assert.ThrowsAsync<KeyNotFoundException>(() =>
//            _sut.SetPermissionsAsync(9, new UpdateRolePermissionsRequest(new[] { PermissionNames.ApiIntegrationMgmt })));
//    }

//    [Test]
//    public async Task SetPermissionsAsync_Replaces_Mapping()
//    {
//        _roles.Setup(r => r.GetByIdTrackedAsync(4, It.IsAny<CancellationToken>()))
//              .ReturnsAsync(new RoleUserMapping { Id = 4, RoleName = RoleName.WorkflowAdmin });

//        _perms.Setup(p => p.GetByNamesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
//              .ReturnsAsync(new List<Permission>
//              {
//                  new Permission { Id = 2, Name = PermissionNames.CreateEditWorkflows },
//                  new Permission { Id = 4, Name = PermissionNames.ViewRespondVerifications }
//              });

//        _perms.Setup(p => p.ReplaceRolePermissionsAsync(4, It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
//              .Returns(Task.CompletedTask);

//        _roles.Setup(r => r.GetByIdNoTrackingAsync(4, It.IsAny<CancellationToken>()))
//              .ReturnsAsync(new RoleUserMapping { Id = 4, RoleName = RoleName.WorkflowAdmin });

//        _perms.Setup(p => p.GetNamesByRoleIdAsync(4, It.IsAny<CancellationToken>()))
//              .ReturnsAsync(new[] {
//                  PermissionNames.CreateEditWorkflows,
//                  PermissionNames.ViewRespondVerifications
//              });

//        var res = await _sut.SetPermissionsAsync(4, new UpdateRolePermissionsRequest(new[]
//        {
//            PermissionNames.CreateEditWorkflows,
//            PermissionNames.ViewRespondVerifications
//        }));

//        Assert.That(res.RoleId, Is.EqualTo(4));
//        Assert.That(res.RoleName, Is.EqualTo(nameof(RoleName.WorkflowAdmin)));
//        CollectionAssert.AreEquivalent(
//            new[] { PermissionNames.CreateEditWorkflows, PermissionNames.ViewRespondVerifications },
//            res.Permissions);
//    }
//}
