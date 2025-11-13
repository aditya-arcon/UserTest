using FluentValidation;
using FluentValidation.Results;
using IDV_Backend.Contracts.Departments;
using IDV_Backend.Models.Departments;
using IDV_Backend.Repositories.Departments;
using IDV_Backend.Services.Departments;
using Moq;
using NUnit.Framework;

namespace UserTest.Services.Departments;

[TestFixture]
public class DepartmentServiceTests
{
    private Mock<IDepartmentRepository> _repo = null!;
    private Mock<IValidator<CreateDepartmentRequest>> _createValidator = null!;
    private Mock<IValidator<UpdateDepartmentRequest>> _updateValidator = null!;
    private DepartmentService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = new Mock<IDepartmentRepository>(MockBehavior.Strict);

        _createValidator = new Mock<IValidator<CreateDepartmentRequest>>(MockBehavior.Strict);
        _createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<CreateDepartmentRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _createValidator
            .Setup(v => v.Validate(It.IsAny<ValidationContext<CreateDepartmentRequest>>()))
            .Returns(new ValidationResult());

        _updateValidator = new Mock<IValidator<UpdateDepartmentRequest>>(MockBehavior.Strict);
        _updateValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<UpdateDepartmentRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _updateValidator
            .Setup(v => v.Validate(It.IsAny<ValidationContext<UpdateDepartmentRequest>>()))
            .Returns(new ValidationResult());

        _sut = new DepartmentService(_repo.Object, _createValidator.Object, _updateValidator.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _repo.VerifyAll();
        _createValidator.VerifyAll();
        _updateValidator.VerifyAll();
    }

    [Test]
    public async Task GetAllAsync_Returns_MappedDtos()
    {
        _repo.Setup(r => r.GetAllNoTrackingAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<Department>
             {
                 new() { DeptId = 1, DeptName = "Finance" },
                 new() { DeptId = 2, DeptName = "Human Resources" },
             });

        var res = await _sut.GetAllAsync();

        Assert.That(res.Count(), Is.EqualTo(2));
        Assert.That(res.First().DeptName, Is.EqualTo("Finance"));
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
             .ReturnsAsync((Department?)null);

        var res = await _sut.GetByIdAsync(99);
        Assert.That(res, Is.Null);
    }

    [Test]
    public async Task GetByIdAsync_Returns_Dto_WhenFound()
    {
        _repo.Setup(r => r.GetByIdNoTrackingAsync(5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Department { DeptId = 5, DeptName = "Ops" });

        var res = await _sut.GetByIdAsync(5);
        Assert.That(res, Is.Not.Null);
        Assert.That(res!.DeptName, Is.EqualTo("Ops"));
    }

    [Test]
    public async Task CreateAsync_Succeeds_WhenUnique_CaseInsensitive()
    {
        var req = new CreateDepartmentRequest("  HuMan   resources ");

        _repo.Setup(r => r.ExistsByNameAsync("human resources", It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        _repo.Setup(r => r.AddAsync(It.IsAny<Department>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(1);

        var dto = await _sut.CreateAsync(req);
        Assert.That(dto.DeptName, Is.EqualTo("HuMan resources")); // normalized (trim + collapse), preserves outer-casing of first token except collapse rule
    }

    [Test]
    public void CreateAsync_Throws_OnDuplicate_CaseInsensitive()
    {
        var req = new CreateDepartmentRequest("Finance");

        _repo.Setup(r => r.ExistsByNameAsync("finance", It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync(req));
    }

    [Test]
    public async Task UpdateAsync_ReturnsNull_WhenMissing()
    {
        _repo.Setup(r => r.GetByIdTrackedAsync(10, It.IsAny<CancellationToken>()))
             .ReturnsAsync((Department?)null);

        var res = await _sut.UpdateAsync(10, new UpdateDepartmentRequest("New Name"));
        Assert.That(res, Is.Null);
    }

    [Test]
    public void UpdateAsync_Throws_OnCollision_CaseInsensitive()
    {
        _repo.Setup(r => r.GetByIdTrackedAsync(2, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Department { DeptId = 2, DeptName = "Old" });

        _repo.Setup(r => r.ExistsByNameOtherIdAsync("finance", 2, It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UpdateAsync(2, new UpdateDepartmentRequest("Finance")));
    }

    [Test]
    public async Task UpdateAsync_Succeeds()
    {
        var entity = new Department { DeptId = 3, DeptName = "Old Name" };

        _repo.Setup(r => r.GetByIdTrackedAsync(3, It.IsAny<CancellationToken>()))
             .ReturnsAsync(entity);

        _repo.Setup(r => r.ExistsByNameOtherIdAsync("new name", 3, It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(1);

        var dto = await _sut.UpdateAsync(3, new UpdateDepartmentRequest("  New   Name "));
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.DeptName, Is.EqualTo("New Name"));
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
             .ReturnsAsync((Department?)null);

        var ok = await _sut.DeleteAsync(50);
        Assert.That(ok, Is.False);
    }

    [Test]
    public async Task DeleteAsync_Succeeds()
    {
        var entity = new Department { DeptId = 4, DeptName = "X" };

        _repo.Setup(r => r.GetByIdTrackedAsync(4, It.IsAny<CancellationToken>()))
             .ReturnsAsync(entity);

        _repo.Setup(r => r.RemoveAsync(entity, It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(1);

        var ok = await _sut.DeleteAsync(4);
        Assert.That(ok, Is.True);
    }
}
