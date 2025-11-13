using FluentValidation;
using FluentValidation.Results;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Models;
using IDV_Backend.Repositories.Roles;
using IDV_Backend.Services.Roles;
using Moq;
using NUnit.Framework;

namespace UserTest.Services.Roles;

[TestFixture]
public class RoleServiceTests
{
    private Mock<IRoleRepository> _repo = null!;
    private Mock<IValidator<CreateRoleRequest>> _createValidator = null!;
    private Mock<IValidator<UpdateRoleRequest>> _updateValidator = null!;
    private Mock<IPermissionRepository> _perms = null!;
    private Mock<IValidator<UpdateRolePermissionsRequest>> _permValidator = null!;
    private Mock<IRolesVersionService> _rolesVersion = null!;
    private RoleService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = new Mock<IRoleRepository>(MockBehavior.Strict);

        _createValidator = new Mock<IValidator<CreateRoleRequest>>(MockBehavior.Strict);
        // Service uses ValidateAndThrowAsync -> this calls ValidateAsync under the hood.
        _createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<CreateRoleRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        // Keep a sync setup too (won't be called, but harmless with Strict as long as we don't VerifyAll)
        _createValidator
            .Setup(v => v.Validate(It.IsAny<ValidationContext<CreateRoleRequest>>()))
            .Returns(new ValidationResult());

        _updateValidator = new Mock<IValidator<UpdateRoleRequest>>(MockBehavior.Strict);
        _updateValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<UpdateRoleRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _updateValidator
            .Setup(v => v.Validate(It.IsAny<ValidationContext<UpdateRoleRequest>>()))
            .Returns(new ValidationResult());

        _perms = new Mock<IPermissionRepository>(MockBehavior.Strict);
        _permValidator = new Mock<IValidator<UpdateRolePermissionsRequest>>(MockBehavior.Strict);
        _rolesVersion = new Mock<IRolesVersionService>(MockBehavior.Strict);

        _sut = new RoleService(
            _repo.Object,
            _perms.Object,
            _createValidator.Object,
            _updateValidator.Object,
            _permValidator.Object,
            _rolesVersion.Object
        );
    }

    [TearDown]
    public void TearDown()
    {
        // Only the repository must be exercised exactly as arranged for each test.
        _repo.VerifyAll();

        // Do NOT VerifyAll() the validators: many tests don't call create/update paths.
        // If you want, you can add explicit Verify(...) inside tests that do call create/update.
    }

    [Test]
    public async Task GetAllAsync_Returns_MappedDtos()
    {
        _repo.Setup(r => r.GetAllNoTrackingAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<RoleUserMapping>
             {
                 new() { Id = 1, RoleName = RoleName.Admin },
                 new() { Id = 2, RoleName = RoleName.User }
             });

        var res = await _sut.GetAllAsync();

        Assert.That(res.Count(), Is.EqualTo(2));
        Assert.That(res.First().Name, Is.EqualTo(nameof(RoleName.Admin)));
    }

    [Test]
    public void GetByIdAsync_Throws_OnInvalidId()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _sut.GetByIdAsync(0));
    }

    [Test]
    public async Task GetByIdAsync_Returns_Null_WhenMissing()
    {
        _repo.Setup(r => r.GetByIdNoTrackingAsync(99, It.IsAny<CancellationToken>()))
             .ReturnsAsync((RoleUserMapping?)null);

        var res = await _sut.GetByIdAsync(99);
        Assert.That(res, Is.Null);
    }

    [Test]
    public async Task GetByIdAsync_Returns_Dto_WhenFound()
    {
        _repo.Setup(r => r.GetByIdNoTrackingAsync(5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new RoleUserMapping { Id = 5, RoleName = RoleName.User });

        var res = await _sut.GetByIdAsync(5);
        Assert.That(res, Is.Not.Null);
        Assert.That(res!.Name, Is.EqualTo(nameof(RoleName.User)));
    }

    [Test]
    public async Task CreateAsync_Succeeds_WhenUnique()
    {
        var req = new CreateRoleRequest(nameof(RoleName.Admin), Enumerable.Empty<string>());

        _repo.Setup(r => r.ExistsByNameAsync(RoleName.Admin, It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        _repo.Setup(r => r.AddAsync(It.IsAny<RoleUserMapping>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(1);

        // Ensure the roles-version bump is allowed by the strict mock
        _rolesVersion.Setup(r => r.BumpAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);

        var dto = await _sut.CreateAsync(req);
        Assert.That(dto.Name, Is.EqualTo(nameof(RoleName.Admin)));

        // Optional: prove async validator was hit on create path
        _createValidator.Verify(v =>
            v.ValidateAsync(It.IsAny<ValidationContext<CreateRoleRequest>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Optional: verify we bumped the roles version
        _rolesVersion.Verify(r => r.BumpAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void CreateAsync_Throws_WhenDuplicate()
    {
        var req = new CreateRoleRequest(nameof(RoleName.Admin), Enumerable.Empty<string>());

        _repo.Setup(r => r.ExistsByNameAsync(RoleName.Admin, It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync(req));

        _createValidator.Verify(v =>
            v.ValidateAsync(It.IsAny<ValidationContext<CreateRoleRequest>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task UpdateAsync_ReturnsNull_WhenMissing()
    {
        _repo.Setup(r => r.GetByIdTrackedAsync(10, It.IsAny<CancellationToken>()))
             .ReturnsAsync((RoleUserMapping?)null);

        var res = await _sut.UpdateAsync(10, new UpdateRoleRequest(nameof(RoleName.User)));
        Assert.That(res, Is.Null);

        _updateValidator.Verify(v =>
            v.ValidateAsync(It.IsAny<ValidationContext<UpdateRoleRequest>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void UpdateAsync_Throws_WhenSuperAdmin()
    {
        _repo.Setup(r => r.GetByIdTrackedAsync(1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new RoleUserMapping { Id = 1, RoleName = RoleName.SuperAdmin });

        Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UpdateAsync(1, new UpdateRoleRequest(nameof(RoleName.Admin))));

        _updateValidator.Verify(v =>
            v.ValidateAsync(It.IsAny<ValidationContext<UpdateRoleRequest>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void UpdateAsync_Throws_OnCollision()
    {
        _repo.Setup(r => r.GetByIdTrackedAsync(2, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new RoleUserMapping { Id = 2, RoleName = RoleName.Admin });

        _repo.Setup(r => r.ExistsByNameOtherIdAsync(RoleName.User, 2, It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UpdateAsync(2, new UpdateRoleRequest(nameof(RoleName.User))));

        _updateValidator.Verify(v =>
            v.ValidateAsync(It.IsAny<ValidationContext<UpdateRoleRequest>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task UpdateAsync_Succeeds()
    {
        _repo.Setup(r => r.GetByIdTrackedAsync(3, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new RoleUserMapping { Id = 3, RoleName = RoleName.Admin });

        _repo.Setup(r => r.ExistsByNameOtherIdAsync(RoleName.User, 3, It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(1);

        // Ensure the roles-version bump is allowed by the strict mock
        _rolesVersion.Setup(r => r.BumpAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);

        var dto = await _sut.UpdateAsync(3, new UpdateRoleRequest(nameof(RoleName.User)));
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Name, Is.EqualTo(nameof(RoleName.User)));

        _updateValidator.Verify(v =>
            v.ValidateAsync(It.IsAny<ValidationContext<UpdateRoleRequest>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Optional: verify we bumped the roles version
        _rolesVersion.Verify(r => r.BumpAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void DeleteAsync_Throws_OnInvalidId()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _sut.DeleteAsync(0));
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenMissing()
    {
        _repo.Setup(r => r.GetByIdTrackedAsync(50, It.IsAny<CancellationToken>()))
             .ReturnsAsync((RoleUserMapping?)null);

        var ok = await _sut.DeleteAsync(50);
        Assert.That(ok, Is.False);
    }

    [Test]
    public void DeleteAsync_Blocks_SuperAdmin()
    {
        _repo.Setup(r => r.GetByIdTrackedAsync(1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new RoleUserMapping { Id = 1, RoleName = RoleName.SuperAdmin });

        Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteAsync(1));
    }

    [Test]
    public void DeleteAsync_Blocks_WhenInUse()
    {
        _repo.Setup(r => r.GetByIdTrackedAsync(2, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new RoleUserMapping { Id = 2, RoleName = RoleName.Admin });

        _repo.Setup(r => r.IsRoleInUseAsync(2, It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteAsync(2));
    }

    [Test]
    public async Task DeleteAsync_Succeeds()
    {
        _repo.Setup(r => r.GetByIdTrackedAsync(3, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new RoleUserMapping { Id = 3, RoleName = RoleName.User });

        _repo.Setup(r => r.IsRoleInUseAsync(3, It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        _repo.Setup(r => r.RemoveAsync(It.IsAny<RoleUserMapping>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(1);

        // Ensure the roles-version bump is allowed by the strict mock
        _rolesVersion.Setup(r => r.BumpAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);

        var ok = await _sut.DeleteAsync(3);
        Assert.That(ok, Is.True);

        // Optional: verify we bumped the roles version
        _rolesVersion.Verify(r => r.BumpAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
