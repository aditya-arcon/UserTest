// UserTest/Repositories/UserTemplateSubmissionValues/UserTemplateSubmissionValueRepositoryTests.cs
using IDV_Backend.Constants;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Models.User;
using IDV_Backend.Models.UserTemplateSubmissions;
using IDV_Backend.Models.UserTemplateSubmissionValue;
using IDV_Backend.Repositories.UserTemplateSubmissionValues;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.UserTemplateSubmissionValues;

[TestFixture]
public class UserTemplateSubmissionValueRepositoryTests
{
    private ApplicationDbContext _db = default!;
    private IUserTemplateSubmissionValueRepository _repo = default!;

    private const long SubmissionId = 1;
    private const long SubmissionIdDeleted = 2;
    private const long TemplateVersionId = 10;
    private const long SectionId1 = 100;
    private const long SectionId2 = 101;

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(opts);

        var nowUtc = DateTime.UtcNow;
        var nowOffset = DateTimeOffset.UtcNow;

        // --- Seed users (parents for Template, TemplateVersion, Submissions, Sections) ---
        var userActive = new User
        {
            Id = 123,
            FirstName = "Active",
            LastName = "User",
            Email = "user123@example.com",
            RoleId = 1,
            ClientReferenceId = 123,
            PublicId = 123,
            PasswordHash = "hash",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            IsActive = true
        };

        var userDeleted = new User
        {
            Id = 456,
            FirstName = "Deleted",
            LastName = "User",
            Email = "user456@example.com",
            RoleId = 1,
            ClientReferenceId = 456,
            PublicId = 456,
            PasswordHash = "hash",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            IsActive = true
        };

        _db.Set<User>().AddRange(userActive, userDeleted);

        // --- Seed template (parent of TemplateVersion) ---
        var template = new Template
        {
            Id = 1,
            Name = "Test Template",
            NameNormalized = "TEST TEMPLATE",
            Mode = TemplateMode.Default,
            Description = null,
            CreatedBy = userActive.Id,
            CreatedAt = nowOffset,
            UpdatedAt = null,
            UpdatedBy = null,
            IsDeleted = false
        };

        _db.Set<Template>().Add(template);

        // --- Seed template version (parent of sections & submissions) ---
        var version = new TemplateVersion
        {
            VersionId = TemplateVersionId,
            TemplateId = template.Id,
            VersionNumber = 1,
            VersionName = "v1",
            Status = TemplateVersionStatus.Draft,
            IsActive = false,
            EnforceRekyc = false,
            RekycDeadline = null,
            ChangeSummary = null,
            RollbackOfVersionId = null,
            IsDeleted = false,
            CreatedBy = userActive.Id,
            UpdatedBy = null,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        };

        _db.Set<TemplateVersion>().Add(version);

        // --- Seed sections (only to keep FK happy; values only use the Ids) ---
        _db.Set<TemplateSection>().AddRange(
            new TemplateSection
            {
                Id = SectionId1,
                TemplateVersionId = TemplateVersionId,
                Name = "A",
                Description = null,
                SectionType = SectionTypes.PersonalInformation,
                OrderIndex = 0,
                IsActive = true,
                CreatedBy = userActive.Id,
                CreatedAt = nowOffset,
                UpdatedAt = null,
                UpdatedBy = null
            },
            new TemplateSection
            {
                Id = SectionId2,
                TemplateVersionId = TemplateVersionId,
                Name = "B",
                Description = null,
                SectionType = SectionTypes.Documents,
                OrderIndex = 1,
                IsActive = true,
                CreatedBy = userActive.Id,
                CreatedAt = nowOffset,
                UpdatedAt = null,
                UpdatedBy = null
            }
        );

        // --- Seed submissions (children referencing TemplateVersion & User) ---
        _db.Set<UserTemplateSubmission>().AddRange(
            new UserTemplateSubmission
            {
                Id = SubmissionId,
                TemplateVersionId = TemplateVersionId,
                UserId = userActive.Id,
                Status = SubmissionStatus.Draft,
                SectionProgress = 0,
                CurrentStep = 0,
                EmailVerified = false,
                PhoneVerified = false,
                StartedAtUtc = null,
                SubmittedAtUtc = null,
                CreatedBy = null,
                UpdatedBy = null,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                IsDeleted = false
            },
            new UserTemplateSubmission
            {
                Id = SubmissionIdDeleted,
                TemplateVersionId = TemplateVersionId,
                UserId = userDeleted.Id,
                Status = SubmissionStatus.Draft,
                SectionProgress = 0,
                CurrentStep = 0,
                EmailVerified = false,
                PhoneVerified = false,
                StartedAtUtc = null,
                SubmittedAtUtc = null,
                CreatedBy = null,
                UpdatedBy = null,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                IsDeleted = true // soft-deleted
            }
        );

        _db.SaveChanges();

        _repo = new UserTemplateSubmissionValueRepository(_db);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task Upsert_Creates_When_Missing()
    {
        var created = await _repo.UpsertAsync(
            SubmissionId,
            SectionId1,
            """{"a":1}""",
            CancellationToken.None);

        Assert.That(created.Id, Is.GreaterThan(0));
        Assert.That(created.UserTemplateSubmissionId, Is.EqualTo(SubmissionId));
        Assert.That(created.TemplateSectionId, Is.EqualTo(SectionId1));
        Assert.That(created.FieldValue, Is.EqualTo("""{"a":1}"""));
        Assert.That(created.CreatedAt, Is.Not.EqualTo(default(DateTime)));
        Assert.That(created.UpdatedAt, Is.Null);
    }

    [Test]
    public async Task Upsert_Updates_When_Exists()
    {
        var first = await _repo.UpsertAsync(SubmissionId, SectionId1, """{"v":1}""", CancellationToken.None);
        var again = await _repo.UpsertAsync(SubmissionId, SectionId1, """{"v":2}""", CancellationToken.None);

        Assert.That(again.Id, Is.EqualTo(first.Id));
        Assert.That(again.FieldValue, Is.EqualTo("""{"v":2}"""));
        Assert.That(again.UpdatedAt, Is.Not.Null);

        var byId = await _repo.GetByIdAsync(first.Id, CancellationToken.None);
        Assert.That(byId!.FieldValue, Is.EqualTo("""{"v":2}"""));
        Assert.That(byId.UpdatedAt, Is.Not.Null);
    }

    [Test]
    public async Task GetById_Returns_Null_For_Unknown()
    {
        var missing = await _repo.GetByIdAsync(9999, CancellationToken.None);
        Assert.That(missing, Is.Null);
    }

    [Test]
    public async Task ListBySubmission_Sorts_By_Section_Then_Id()
    {
        var v2 = await _repo.UpsertAsync(SubmissionId, SectionId2, """{"k":"b"}""", CancellationToken.None);
        var v1 = await _repo.UpsertAsync(SubmissionId, SectionId1, """{"k":"a"}""", CancellationToken.None);

        var list = await _repo.GetBySubmissionAsync(SubmissionId, CancellationToken.None);

        Assert.That(list.Count, Is.EqualTo(2));
        Assert.That(list[0].TemplateSectionId, Is.EqualTo(SectionId1));
        Assert.That(list[1].TemplateSectionId, Is.EqualTo(SectionId2));
        Assert.That(list.Select(x => x.Id), Is.EqualTo(new[] { v1.Id, v2.Id }));
    }

    [Test]
    public async Task FindBySubmissionAndSection_Returns_Most_Recently_Updated()
    {
        var a = await _repo.UpsertAsync(SubmissionId, SectionId1, """{"v":1}""", CancellationToken.None);
        await Task.Delay(5);
        var b = await _repo.UpsertAsync(SubmissionId, SectionId1, """{"v":2}""", CancellationToken.None);

        var latest = await _repo.GetBySubmissionAndSectionAsync(SubmissionId, SectionId1, CancellationToken.None);

        Assert.That(latest, Is.Not.Null);
        Assert.That(latest!.Id, Is.EqualTo(b.Id));
        Assert.That(latest.FieldValue, Is.EqualTo("""{"v":2}"""));
    }

    [Test]
    public async Task Remove_Deletes_Row()
    {
        var created = await _repo.UpsertAsync(SubmissionId, SectionId2, """{"z":9}""", CancellationToken.None);

        var ok = await _repo.DeleteAsync(created.Id, CancellationToken.None);
        Assert.That(ok, Is.True);

        var after = await _repo.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.That(after, Is.Null);
    }

    [Test]
    public async Task ListBySubmission_Respects_SoftDelete_Filter()
    {
        await _repo.UpsertAsync(SubmissionIdDeleted, SectionId1, """{"x":1}""", CancellationToken.None);

        var active = await _repo.GetBySubmissionAsync(SubmissionId, CancellationToken.None);
        var deleted = await _repo.GetBySubmissionAsync(SubmissionIdDeleted, CancellationToken.None);

        Assert.That(active, Is.Not.Null);
        Assert.That(deleted.Count, Is.EqualTo(0));
    }
}