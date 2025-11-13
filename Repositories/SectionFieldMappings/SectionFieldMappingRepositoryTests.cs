using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.TemplateSectionMappings;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Repositories.SectionFieldMappings;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.SectionFieldMappings;

[TestFixture]
public sealed class SectionFieldMappingRepositoryTests
{
    private ApplicationDbContext _db = default!;
    private ISectionFieldMappingRepository _repo = default!;
    private long _sectionId;

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(opts);

        // seed version + section
        _db.TemplateVersions.Add(new TemplateVersion
        {
            VersionId = 5,
            TemplateId = 1,
            VersionNumber = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var s = new TemplateSection
        {
            TemplateVersionId = 5,
            Name = "PI",
            SectionType = "personalInformation",
            OrderIndex = 1,
            IsActive = true,
            CreatedBy = 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.TemplateSections.Add(s);
        _db.SaveChanges();
        _sectionId = s.Id;

        _repo = new SectionFieldMappingRepository(_db);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task Add_List_Get_Remove_Work()
    {
        var m = new SectionFieldMapping
        {
            TemplateSectionId = _sectionId,
            Structure = "{\"a\":1}",
            CaptureAllowed = true,
            UploadAllowed = false
        };

        await _repo.AddAsync(m, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var list = await _repo.ListBySectionAsync(_sectionId, CancellationToken.None);
        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].Id, Is.EqualTo(m.Id));

        var byId = await _repo.GetByIdAsync(m.Id, CancellationToken.None);
        Assert.That(byId, Is.Not.Null);

        await _repo.RemoveAsync(m, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var again = await _repo.GetByIdAsync(m.Id, CancellationToken.None);
        Assert.That(again, Is.Null);
    }

    [Test]
    public async Task RemoveBySection_Deletes_All()
    {
        await _repo.AddAsync(new SectionFieldMapping { TemplateSectionId = _sectionId, Structure = "{}", CaptureAllowed = false, UploadAllowed = false }, CancellationToken.None);
        await _repo.AddAsync(new SectionFieldMapping { TemplateSectionId = _sectionId, Structure = "{\"b\":2}" }, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        await _repo.RemoveBySectionAsync(_sectionId, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var list = await _repo.ListBySectionAsync(_sectionId, CancellationToken.None);
        Assert.That(list, Is.Empty);
    }
}
