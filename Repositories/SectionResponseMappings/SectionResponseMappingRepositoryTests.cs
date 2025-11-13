using IDV_Backend.Data;
using IDV_Backend.Models.SectionResponseMapping;
using IDV_Backend.Repositories.SectionResponseMappings;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.SectionResponseMappings;

[TestFixture]
public class SectionResponseMappingRepositoryTests
{
    private ApplicationDbContext _db = default!;
    private SectionResponseMappingRepository _repo = default!;

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(opts);
        _repo = new SectionResponseMappingRepository(_db);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task Add_And_FindById_Works()
    {
        var e = new SectionResponseMapping
        {
            UserTemplateSubmissionRef = 1001,
            TemplateSectionRef = 2001,
            IsCompleted = false,
            LastUpdatedAtUtc = DateTime.UtcNow
        };

        await _repo.AddAsync(e);
        await _repo.SaveChangesAsync();

        var fetched = await _repo.FindByIdAsync(e.Id);
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched!.TemplateSectionRef, Is.EqualTo(2001));
    }

    [Test]
    public async Task ListBySubmission_Sorts_By_TemplateSectionRef()
    {
        await _repo.AddAsync(new SectionResponseMapping { UserTemplateSubmissionRef = 999, TemplateSectionRef = 30, IsCompleted = false, LastUpdatedAtUtc = DateTime.UtcNow });
        await _repo.AddAsync(new SectionResponseMapping { UserTemplateSubmissionRef = 999, TemplateSectionRef = 10, IsCompleted = false, LastUpdatedAtUtc = DateTime.UtcNow });
        await _repo.AddAsync(new SectionResponseMapping { UserTemplateSubmissionRef = 777, TemplateSectionRef = 20, IsCompleted = false, LastUpdatedAtUtc = DateTime.UtcNow });
        await _repo.SaveChangesAsync();

        var list = await _repo.ListBySubmissionAsync(999);
        Assert.That(list.Select(x => x.TemplateSectionRef), Is.EqualTo(new[] { 10L, 30L }));
    }

    [Test]
    public async Task ExistsBySubmissionAndSection_Works()
    {
        await _repo.AddAsync(new SectionResponseMapping { UserTemplateSubmissionRef = 55, TemplateSectionRef = 7, IsCompleted = false, LastUpdatedAtUtc = DateTime.UtcNow });
        await _repo.SaveChangesAsync();

        var exists = await _repo.ExistsBySubmissionAndSectionAsync(55, 7);
        var notExists = await _repo.ExistsBySubmissionAndSectionAsync(55, 8);

        Assert.That(exists, Is.True);
        Assert.That(notExists, Is.False);
    }

    [Test]
    public async Task Update_And_Remove_Work()
    {
        var e = new SectionResponseMapping { UserTemplateSubmissionRef = 42, TemplateSectionRef = 1, IsCompleted = false, LastUpdatedAtUtc = DateTime.UtcNow };
        await _repo.AddAsync(e);
        await _repo.SaveChangesAsync();

        e.IsCompleted = true;
        e.LastUpdatedAtUtc = DateTime.UtcNow.AddMinutes(1);
        await _repo.UpdateAsync(e);
        await _repo.SaveChangesAsync();

        var afterUpdate = await _repo.FindByIdAsync(e.Id);
        Assert.That(afterUpdate!.IsCompleted, Is.True);

        await _repo.RemoveAsync(e);
        await _repo.SaveChangesAsync();

        var afterRemove = await _repo.FindByIdAsync(e.Id);
        Assert.That(afterRemove, Is.Null);
    }
}
