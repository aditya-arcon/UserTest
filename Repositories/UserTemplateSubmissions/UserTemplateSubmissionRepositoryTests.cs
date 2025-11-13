using IDV_Backend.Data;
using IDV_Backend.Models.User;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Models.UserTemplateSubmissions;
using IDV_Backend.Repositories.UserTemplateSubmissions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.UserTemplateSubmissions;

[TestFixture]
public class UserTemplateSubmissionRepositoryTests
{
    private ApplicationDbContext _db = default!;
    private IUserTemplateSubmissionRepository _repo = default!;

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(opts);

        // Seed minimal FK rows
        _db.Set<User>().Add(new User { Id = 1, Email = "a@b.com", Phone = "1" });

        // 👇 TemplateVersion has VersionId (no Id property)
        _db.Set<TemplateVersion>().Add(new TemplateVersion
        {
            VersionId = 1000,
            IsDeleted = false
        });

        _db.SaveChanges();

        _repo = new UserTemplateSubmissionRepository(_db);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task Add_Find_Update_SoftDelete_Restore_Works()
    {
        var s = new UserTemplateSubmission
        {
            TemplateVersionId = 1000,
            UserId = 1,
            Status = SubmissionStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        await _repo.AddAsync(s, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var got = await _repo.FindByIdAsync(s.Id, CancellationToken.None);
        Assert.That(got, Is.Not.Null);
        Assert.That(got!.IsDeleted, Is.False);

        s.Status = SubmissionStatus.Active;
        await _repo.UpdateAsync(s, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var afterUpdate = await _repo.FindByIdAsync(s.Id, CancellationToken.None);
        Assert.That(afterUpdate!.Status, Is.EqualTo(SubmissionStatus.Active));

        await _repo.SoftDeleteAsync(s, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var soft = await _repo.FindByIdAsync(s.Id, CancellationToken.None);
        Assert.That(soft, Is.Null); // filtered by query filter

        var softRaw = await _repo.FindSoftDeletedByIdAsync(s.Id, CancellationToken.None);
        Assert.That(softRaw, Is.Not.Null);
        Assert.That(softRaw!.IsDeleted, Is.True);

        await _repo.RestoreAsync(softRaw, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var restored = await _repo.FindByIdAsync(s.Id, CancellationToken.None);
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.IsDeleted, Is.False);
    }

    [Test]
    public async Task FindByUserAndTemplateVersion_Respects_IncludeSoftDeleted()
    {
        var s = new UserTemplateSubmission
        {
            TemplateVersionId = 1000,
            UserId = 1,
            Status = SubmissionStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        await _repo.AddAsync(s, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        await _repo.SoftDeleteAsync(s, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var none = await _repo.FindByUserAndTemplateVersionAsync(1, 1000, includeSoftDeleted: false, CancellationToken.None);
        var yes = await _repo.FindByUserAndTemplateVersionAsync(1, 1000, includeSoftDeleted: true, CancellationToken.None);

        Assert.That(none, Is.Null);
        Assert.That(yes, Is.Not.Null);
        Assert.That(yes!.IsDeleted, Is.True);
    }

    [Test]
    public async Task Search_Paginates_And_Sorts()
    {
        for (int i = 0; i < 30; i++)
        {
            await _repo.AddAsync(new UserTemplateSubmission
            {
                TemplateVersionId = 1000,
                UserId = 1,
                Status = i % 2 == 0 ? SubmissionStatus.Active : SubmissionStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i),
                UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-i)
            }, CancellationToken.None);
        }
        await _repo.SaveChangesAsync(CancellationToken.None);

        var page1 = await _repo.SearchAsync(new()
        {
            Page = 1,
            PageSize = 10,
            SortBy = "createdAt",
            SortDir = "desc"
        }, CancellationToken.None);

        var page2 = await _repo.SearchAsync(new()
        {
            Page = 2,
            PageSize = 10,
            SortBy = "createdAt",
            SortDir = "desc"
        }, CancellationToken.None);

        Assert.That(page1.Items.Count, Is.EqualTo(10));
        Assert.That(page2.Items.Count, Is.EqualTo(10));
        Assert.That(page1.TotalCount, Is.EqualTo(30));
    }
}
