using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Repositories.TemplateSections;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.TemplateSections;

[TestFixture]
public class TemplateSectionRepositoryTests
{
    private ApplicationDbContext _db = default!;
    private ITemplateSectionRepository _repo = default!;

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(opts);

        // Seed version + sections
        _db.TemplateVersions.Add(new TemplateVersion
        {
            VersionId = 5,
            TemplateId = 1,
            VersionNumber = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.TemplateSections.AddRange(
            new TemplateSection { TemplateVersionId = 5, Name = "A", SectionType = "personalInformation", OrderIndex = 1, IsActive = true, CreatedBy = 1, CreatedAt = DateTimeOffset.UtcNow },
            new TemplateSection { TemplateVersionId = 5, Name = "B", SectionType = "documents", OrderIndex = 2, IsActive = true, CreatedBy = 1, CreatedAt = DateTimeOffset.UtcNow },
            new TemplateSection { TemplateVersionId = 5, Name = "C", SectionType = "biometrics", OrderIndex = 3, IsActive = false, CreatedBy = 1, CreatedAt = DateTimeOffset.UtcNow }
        );
        _db.SaveChanges();

        _repo = new TemplateSectionRepository(_db);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task GetByVersion_All_Orders_ActiveFirst()
    {
        var all = await _repo.GetByVersionAsync(5, activeOnly: false, CancellationToken.None);
        Assert.That(all.Count, Is.EqualTo(3));
        Assert.That(all[0].IsActive, Is.True);
        Assert.That(all[1].IsActive, Is.True);
        Assert.That(all[2].IsActive, Is.False);
    }

    [Test]
    public async Task GetByVersion_ActiveOnly_SortedByOrder()
    {
        var active = await _repo.GetByVersionAsync(5, activeOnly: true, CancellationToken.None);
        Assert.That(active.Count, Is.EqualTo(2));
        Assert.That(active[0].OrderIndex, Is.EqualTo(1));
        Assert.That(active[1].OrderIndex, Is.EqualTo(2));
    }

    [Test]
    public async Task AnyActiveOfType_Works()
    {
        var yes = await _repo.AnyActiveOfTypeAsync(5, "personalInformation", null, CancellationToken.None);
        var no = await _repo.AnyActiveOfTypeAsync(5, "biometrics", null, CancellationToken.None);

        Assert.That(yes, Is.True);
        Assert.That(no, Is.False);
    }

    [Test]
    public async Task UpdateAsync_And_RemoveAsync_Work()
    {
        var first = (await _repo.GetByVersionAsync(5, activeOnly: false, CancellationToken.None)).First();

        first.Name = "Renamed";
        await _repo.UpdateAsync(first, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var again = await _repo.GetByIdAsync(first.Id, CancellationToken.None);
        Assert.That(again!.Name, Is.EqualTo("Renamed"));

        await _repo.RemoveAsync(first, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var gone = await _repo.GetByIdAsync(first.Id, CancellationToken.None);
        Assert.That(gone, Is.Null);
    }
}
