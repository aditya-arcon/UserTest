using FluentValidation;
using FluentValidation.Results;
using IDV_Backend.Contracts.SectionResponseMappings;
using IDV_Backend.Contracts.SectionResponseMappings.Validators;
using IDV_Backend.Data;
using IDV_Backend.Repositories.SectionResponseMappings;
using IDV_Backend.Services.SectionResponseMappings;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Services.SectionResponseMappings;

[TestFixture]
public class SectionResponseMappingServiceTests
{
    private ApplicationDbContext _db = default!;
    private ISectionResponseMappingRepository _repo = default!;
    private ISectionResponseMappingService _svc = default!;

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(opts);
        _repo = new SectionResponseMappingRepository(_db);

        // Use the real validators you shipped
        IValidator<CreateSectionResponseMappingRequest> createValidator = new CreateSectionResponseMappingRequestValidator();
        IValidator<UpdateCompletionRequest> updateValidator = new UpdateCompletionRequestValidator();

        _svc = new SectionResponseMappingService(_repo, createValidator, updateValidator);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task Create_Is_Idempotent_On_Submission_And_Section()
    {
        var req = new CreateSectionResponseMappingRequest(UserTemplateSubmissionId: 100, TemplateSectionId: 200);

        var first = await _svc.CreateAsync(req, CancellationToken.None);
        var second = await _svc.CreateAsync(req, CancellationToken.None);

        Assert.That(first.Id, Is.EqualTo(second.Id));
        Assert.That(second.UserTemplateSubmissionId, Is.EqualTo(100));
        Assert.That(second.TemplateSectionId, Is.EqualTo(200));
        Assert.That(second.IsCompleted, Is.False);
    }

    [Test]
    public async Task SetCompletion_Updates_Flag_And_Timestamp()
    {
        var created = await _svc.CreateAsync(new CreateSectionResponseMappingRequest(100, 201), CancellationToken.None);

        var before = await _svc.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.That(before, Is.Not.Null);
        var updated = await _svc.SetCompletionAsync(created.Id, new UpdateCompletionRequest(IsCompleted: true), CancellationToken.None);

        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.IsCompleted, Is.True);
        Assert.That(updated.LastUpdatedAtUtc, Is.GreaterThan(before!.LastUpdatedAtUtc));
    }

    [Test]
    public async Task GetBySubmission_Returns_Sorted_Results()
    {
        await _svc.CreateAsync(new CreateSectionResponseMappingRequest(999, 30), CancellationToken.None);
        await _svc.CreateAsync(new CreateSectionResponseMappingRequest(999, 10), CancellationToken.None);
        await _svc.CreateAsync(new CreateSectionResponseMappingRequest(777, 20), CancellationToken.None);

        var list = await _svc.GetBySubmissionAsync(999, CancellationToken.None);
        Assert.That(list.Select(x => x.TemplateSectionId), Is.EqualTo(new[] { 10L, 30L }));
    }

    [Test]
    public async Task Delete_Removes_Entity()
    {
        var created = await _svc.CreateAsync(new CreateSectionResponseMappingRequest(500, 700), CancellationToken.None);

        var ok = await _svc.DeleteAsync(created.Id, CancellationToken.None);
        Assert.That(ok, Is.True);

        var after = await _svc.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.That(after, Is.Null);
    }

    [Test]
    public void Create_Throws_On_Invalid_Request()
    {
        var bad = new CreateSectionResponseMappingRequest(UserTemplateSubmissionId: 0, TemplateSectionId: -1);
        Assert.ThrowsAsync<ValidationException>(async () => await _svc.CreateAsync(bad, CancellationToken.None));
    }
}
