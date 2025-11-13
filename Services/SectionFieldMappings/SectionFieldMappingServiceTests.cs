using FluentValidation;
using IDV_Backend.Contracts.SectionFieldMapping;
using IDV_Backend.Contracts.SectionFieldMapping.Validators;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Repositories.SectionFieldMappings;
using IDV_Backend.Repositories.TemplateSections;
using IDV_Backend.Repositories.TemplateVersions;
using IDV_Backend.Services.Audit;
using IDV_Backend.Services.Common;
using IDV_Backend.Services.SectionFieldMappingServices;
using IDV_Backend.Services.TemplateVersion;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
using System.Text.Json;

namespace UserTest.Services.SectionFieldMappings;

[TestFixture]
public sealed class SectionFieldMappingServiceTests
{
    private ApplicationDbContext _db = default!;
    private ISectionFieldMappingRepository _repo = default!;
    private ITemplateSectionRepository _sectionRepo = default!;
    private ITemplateVersionRepository _versionRepo = default!;
    private SectionFieldMappingService _svc = default!;
    private Mock<ICurrentUser> _me = default!;
    private Mock<ITemplateAuditLogger> _audit = default!;
    private Mock<ITemplateVersionService> _versionSvc = default!;
    private long _sectionIdDraft;

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(opts);

        // seed template version (Draft) + section
        _db.TemplateVersions.Add(new TemplateVersion
        {
            VersionId = 10,
            TemplateId = 1,
            VersionNumber = 1,
            Status = TemplateVersionStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var s = new TemplateSection
        {
            TemplateVersionId = 10,
            Name = "Docs",
            SectionType = "documents",
            IsActive = true,
            OrderIndex = 1,
            CreatedBy = 7,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.TemplateSections.Add(s);
        _db.Users.Add(new IDV_Backend.Models.User.User { Id = 7, Email = "x@y", FirstName = "X", LastName = "Y" });
        _db.SaveChanges();
        _sectionIdDraft = s.Id;

        _repo = new SectionFieldMappingRepository(_db);
        _sectionRepo = new TemplateSectionRepository(_db);
        _versionRepo = new TemplateVersionRepository(_db);

        _me = new Mock<ICurrentUser>();
        _me.SetupGet(x => x.UserId).Returns(7);
        _me.SetupGet(x => x.UserName).Returns("X Y");

        _audit = new Mock<ITemplateAuditLogger>();
        _audit.Setup(x => x.LogAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        _versionSvc = new Mock<ITemplateVersionService>();
        // By default return a no-op fork map (not used for Draft anyway)
        _versionSvc.Setup(x => x.ForkWithMappingAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new VersionForkResult
                   {
                       TargetVersionId = 999,
                       SectionIdMap = new Dictionary<long, long>()
                   });

        _svc = new SectionFieldMappingService(
            _repo,
            _sectionRepo,
            _versionRepo,
            _audit.Object,
            _me.Object,
            new CreateSectionFieldMappingDtoValidator(_db),
            new UpdateSectionFieldMappingDtoValidator(),
            _versionSvc.Object
        );
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task Create_Then_Get_Works()
    {
        var created = await _svc.CreateAsync(new CreateSectionFieldMappingDto
        {
            TemplateSectionId = _sectionIdDraft,
            Structure = J("""{"a":1}"""),
            CaptureAllowed = true,
            UploadAllowed = false
        }, CancellationToken.None);

        Assert.That(created.Id, Is.GreaterThan(0));
        Assert.That(created.TemplateSectionId, Is.EqualTo(_sectionIdDraft));
        Assert.That(created.CaptureAllowed, Is.True);

        var byId = await _svc.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.That(byId, Is.Not.Null);
        Assert.That(byId!.Id, Is.EqualTo(created.Id));
    }

    [Test]
    public async Task GetBySection_Returns_List()
    {
        await _svc.CreateAsync(new CreateSectionFieldMappingDto { TemplateSectionId = _sectionIdDraft, Structure = J("""{"x":1}""") }, CancellationToken.None);
        await _svc.CreateAsync(new CreateSectionFieldMappingDto { TemplateSectionId = _sectionIdDraft, Structure = J("""{"y":2}""") }, CancellationToken.None);

        var list = (await _svc.GetBySectionIdAsync(_sectionIdDraft, CancellationToken.None)).ToList();
        Assert.That(list.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Update_Modifies_Fields()
    {
        var created = await _svc.CreateAsync(new CreateSectionFieldMappingDto
        {
            TemplateSectionId = _sectionIdDraft,
            Structure = J("""{"a":1}"""),
            CaptureAllowed = false,
            UploadAllowed = false
        }, CancellationToken.None);

        var updated = await _svc.UpdateAsync(created.Id, new UpdateSectionFieldMappingDto
        {
            Structure = J("""{"a":2}"""),
            CaptureAllowed = true
        }, CancellationToken.None);

        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Structure!.Value.GetProperty("a").GetInt32(), Is.EqualTo(2));
        Assert.That(updated.CaptureAllowed, Is.True);
    }

    [Test]
    public async Task PatchBySection_Creates_When_Missing()
    {
        var res = await _svc.PatchBySectionIdAsync(_sectionIdDraft, new UpdateSectionFieldMappingDto
        {
            Structure = J("""{"m":9}"""),
            UploadAllowed = true
        }, CancellationToken.None);

        Assert.That(res, Is.Not.Null);
        Assert.That(res!.Structure!.Value.GetProperty("m").GetInt32(), Is.EqualTo(9));
        Assert.That(res.UploadAllowed, Is.True);
    }

    [Test]
    public async Task Delete_Removes_Row()
    {
        var created = await _svc.CreateAsync(new CreateSectionFieldMappingDto
        {
            TemplateSectionId = _sectionIdDraft,
            Structure = J("""{"a":1}""")
        }, CancellationToken.None);

        var ok = await _svc.DeleteAsync(created.Id, CancellationToken.None);
        Assert.That(ok, Is.True);

        var after = await _svc.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.That(after, Is.Null);
    }
}
