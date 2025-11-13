using FluentValidation;
using IDV_Backend.Contracts.UserTemplateSubmissionValues;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.UserTemplateSubmissions;
using IDV_Backend.Repositories.UserTemplateSubmissionValues;
using IDV_Backend.Services.Reports;
using IDV_Backend.Services.UserTemplateSubmissionValues;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Text.Json;

namespace UserTest.Services.UserTemplateSubmissionValues;

[TestFixture]
public class UserTemplateSubmissionValueServiceTests
{
    private ApplicationDbContext _db = default!;
    private IUserTemplateSubmissionValueRepository _repo = default!;
    private IUserTemplateSubmissionValueService _svc = default!;
    private IValidator<CreateUserTemplateSubmissionValueRequest> _createValidator = default!;
    private IValidator<UpdateUserTemplateSubmissionValueRequest> _updateValidator = default!;

    private const long SubmissionId = 1;
    private const long TemplateVersionId_OK = 10;
    private const long TemplateVersionId_Bad = 99;
    private const long SectionId_OK_1 = 100;
    private const long SectionId_OK_2 = 101;
    private const long SectionId_Bad = 200;

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(opts);

        // Seed one active submission (not soft-deleted)
        _db.Set<UserTemplateSubmission>().Add(new UserTemplateSubmission
        {
            Id = SubmissionId,
            TemplateVersionId = TemplateVersionId_OK,
            UserId = 123,
            Status = SubmissionStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsDeleted = false
        });

        // Seed template sections that belong to the same version
        _db.Set<TemplateSection>().AddRange(
            new TemplateSection { Id = SectionId_OK_1, TemplateVersionId = TemplateVersionId_OK, Name = "A" },
            new TemplateSection { Id = SectionId_OK_2, TemplateVersionId = TemplateVersionId_OK, Name = "B" }
        );

        // Seed a section that belongs to a different version (to trigger mismatch)
        _db.Set<TemplateSection>().Add(
            new TemplateSection { Id = SectionId_Bad, TemplateVersionId = TemplateVersionId_Bad, Name = "X" }
        );

        _db.SaveChanges();

        _repo = new UserTemplateSubmissionValueRepository(_db);

        _createValidator = new CreateUserTemplateSubmissionValueRequestValidator();
        _updateValidator = new UpdateUserTemplateSubmissionValueRequestValidator();

        // Add these two lines to create mocks for the required dependencies
        var mockActivityLogger = new Mock<IUserActivityLogger>().Object;
        var mockRetryTracker = new Mock<ISectionRetryTracker>().Object;

        // Update the _svc initialization to include the required parameters
        _svc = new UserTemplateSubmissionValueService(
            _db,
            _repo,
            mockActivityLogger,
            mockRetryTracker,
            _createValidator,
            _updateValidator
        );
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task Create_Creates_New_Value()
    {
        var req = new CreateUserTemplateSubmissionValueRequest(FieldValue: J("""{"first":"value"}"""));
        var created = await _svc.CreateAsync(SubmissionId, SectionId_OK_1, req, CancellationToken.None);

        Assert.That(created.Id, Is.GreaterThan(0));
        Assert.That(created.UserTemplateSubmissionId, Is.EqualTo(SubmissionId));
        Assert.That(created.TemplateSectionId, Is.EqualTo(SectionId_OK_1));
        Assert.That(created.FieldValue.GetProperty("first").GetString(), Is.EqualTo("value"));
        Assert.That(created.UpdatedAt, Is.Null);
    }

    [Test]
    public async Task Create_Upserts_When_Row_Exists_For_Submission_And_Section()
    {
        var first = await _svc.CreateAsync(
            SubmissionId,
            SectionId_OK_1,
            new CreateUserTemplateSubmissionValueRequest(J("""{"v":1}""")),
            CancellationToken.None
        );

        // call Create again on same (submission, section) — should update not create new row
        var second = await _svc.CreateAsync(
            SubmissionId,
            SectionId_OK_1,
            new CreateUserTemplateSubmissionValueRequest(J("""{"v":2}""")),
            CancellationToken.None
        );

        Assert.That(second.Id, Is.EqualTo(first.Id)); // upsert
        Assert.That(second.FieldValue.GetProperty("v").GetInt32(), Is.EqualTo(2));

        // verify UpdatedAt set
        var byId = await _svc.GetByIdAsync(first.Id, CancellationToken.None);
        Assert.That(byId!.UpdatedAt, Is.Not.Null);
    }

    [Test]
    public async Task GetBySubmission_Returns_Sorted_By_Section_Then_Id()
    {
        // create values in reverse section order to check sorting
        await _svc.CreateAsync(SubmissionId, SectionId_OK_2, new CreateUserTemplateSubmissionValueRequest(J("""{"k":"b"}""")), CancellationToken.None);
        await _svc.CreateAsync(SubmissionId, SectionId_OK_1, new CreateUserTemplateSubmissionValueRequest(J("""{"k":"a"}""")), CancellationToken.None);

        var list = await _svc.GetBySubmissionAsync(SubmissionId, CancellationToken.None);

        Assert.That(list.Count, Is.EqualTo(2));
        Assert.That(list[0].TemplateSectionId, Is.EqualTo(SectionId_OK_1));
        Assert.That(list[1].TemplateSectionId, Is.EqualTo(SectionId_OK_2));
    }

    [Test]
    public async Task GetBySubmissionAndSection_Returns_Most_Recent()
    {
        // create twice for same (sub, section) to ensure we get newest by UpdatedAt/CreatedAt
        var a = await _svc.CreateAsync(SubmissionId, SectionId_OK_1, new CreateUserTemplateSubmissionValueRequest(J("""{"v":1}""")), CancellationToken.None);
        await Task.Delay(10); // ensure timestamp difference
        var b = await _svc.CreateAsync(SubmissionId, SectionId_OK_1, new CreateUserTemplateSubmissionValueRequest(J("""{"v":2}""")), CancellationToken.None);

        var latest = await _svc.GetBySubmissionAndSectionAsync(SubmissionId, SectionId_OK_1, CancellationToken.None);
        Assert.That(latest, Is.Not.Null);
        Assert.That(latest!.Id, Is.EqualTo(b.Id));
        Assert.That(latest.FieldValue.GetProperty("v").GetInt32(), Is.EqualTo(2));
    }

    [Test]
    public async Task Update_Changes_FieldValue_And_Sets_UpdatedAt()
    {
        var created = await _svc.CreateAsync(
            SubmissionId,
            SectionId_OK_2,
            new CreateUserTemplateSubmissionValueRequest(J("""{"x":1}""")),
            CancellationToken.None
        );

        var before = await _svc.GetByIdAsync(created.Id, CancellationToken.None);

        var updated = await _svc.UpdateAsync(
            created.Id,
            new UpdateUserTemplateSubmissionValueRequest(J("""{"x":2}""")),
            CancellationToken.None
        );

        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.FieldValue.GetProperty("x").GetInt32(), Is.EqualTo(2));
        Assert.That(updated.UpdatedAt, Is.Not.Null);

        // If this is the first update, before.UpdatedAt is null. Compare to CreatedAt instead.
        if (before!.UpdatedAt.HasValue)
            Assert.That(updated.UpdatedAt!.Value, Is.GreaterThan(before.UpdatedAt!.Value));
        else
            Assert.That(updated.UpdatedAt!.Value, Is.GreaterThan(before.CreatedAt));
    }


    [Test]
    public async Task Delete_Removes_Row()
    {
        var created = await _svc.CreateAsync(SubmissionId, SectionId_OK_1, new CreateUserTemplateSubmissionValueRequest(J("""{"z":9}""")), CancellationToken.None);
        var ok = await _svc.DeleteAsync(created.Id, CancellationToken.None);
        Assert.That(ok, Is.True);

        var after = await _svc.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.That(after, Is.Null);
    }

    [Test]
    public void Create_Throws_When_Section_Does_Not_Belong_To_Submission_Version()
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _svc.CreateAsync(
                SubmissionId,
                SectionId_Bad, // mismatched templateVersionId
                new CreateUserTemplateSubmissionValueRequest(J("""{"oops":true}""")),
                CancellationToken.None
            );
        });

        StringAssert.Contains($"does not belong to submission {SubmissionId}", ex!.Message);
    }

    [Test]
    public void Create_Rejects_Empty_Json()
    {
        // Empty object should fail due to validator rule (no properties)
        Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await _svc.CreateAsync(
                SubmissionId,
                SectionId_OK_1,
                new CreateUserTemplateSubmissionValueRequest(J("""{}""")),
                CancellationToken.None
            );
        });

        // null should fail
        Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await _svc.CreateAsync(
                SubmissionId,
                SectionId_OK_1,
                new CreateUserTemplateSubmissionValueRequest(default),
                CancellationToken.None
            );
        });
    }

    [Test]
    public async Task Update_Rejects_Empty_Json()
    {
        var created = await _svc.CreateAsync(
            SubmissionId,
            SectionId_OK_1,
            new CreateUserTemplateSubmissionValueRequest(J("""{"a":1}""")),
            CancellationToken.None
        );

        Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await _svc.UpdateAsync(
                created.Id,
                new UpdateUserTemplateSubmissionValueRequest(J("""[]""")), // empty array -> invalid
                CancellationToken.None
            );
        });
    }

    // --- helpers ---

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
