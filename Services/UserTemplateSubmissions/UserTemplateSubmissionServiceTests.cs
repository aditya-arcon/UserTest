using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Contracts.UserTemplateSubmissions;
using IDV_Backend.Data;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Models.User;
using IDV_Backend.Repositories.UserTemplateSubmissions;
using IDV_Backend.Services.Common;
using IDV_Backend.Services.Reports;
using IDV_Backend.Services.UserTemplateSubmissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace UserTest.Services.UserTemplateSubmissions;

[TestFixture]
public class UserTemplateSubmissionServiceTests
{
    private ApplicationDbContext _db = default!;
    private IUserTemplateSubmissionRepository _repo = default!;
    private IUserTemplateSubmissionService _svc = default!;
    private Mock<ICurrentUser> _me = default!;
    private Mock<IUserActivityLogger> _activityLogger = default!;

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(opts);

        // Seed users 1..40 so we can create many unique (UserId, TemplateVersionId) pairs
        for (int i = 1; i <= 40; i++)
        {
            _db.Set<User>().Add(new User { Id = i, Email = $"user{i}@idv.local", Phone = $"9{i:D2}" });
        }

        // TemplateVersion has VersionId (no Id property)
        _db.Set<TemplateVersion>().Add(new TemplateVersion
        {
            VersionId = 1000,
            IsDeleted = false
        });

        _db.SaveChanges();

        _repo = new UserTemplateSubmissionRepository(_db);

        _me = new Mock<ICurrentUser>();
        _me.SetupGet(x => x.UserId).Returns(777);

        _activityLogger = new Mock<IUserActivityLogger>();

        var loggerMock = new Mock<ILogger<UserTemplateSubmissionService>>();
        _svc = new UserTemplateSubmissionService(_repo, _me.Object, _activityLogger.Object, _db, loggerMock.Object);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task Create_Then_Get_Works()
    {
        var created = await _svc.CreateAsync(new CreateUserTemplateSubmissionRequest
        {
            TemplateVersionId = 1000,
            UserId = 1
        }, CancellationToken.None);

        Assert.That(created.Id, Is.GreaterThan(0));
        Assert.That(created.Status.ToString(), Is.EqualTo("Draft"));

        var byId = await _svc.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.That(byId, Is.Not.Null);
        Assert.That(byId!.Id, Is.EqualTo(created.Id));
    }

    [Test]
    public async Task Create_Refuses_Duplicates_On_User_TemplateVersion()
    {
        await _svc.CreateAsync(new CreateUserTemplateSubmissionRequest { TemplateVersionId = 1000, UserId = 1 }, CancellationToken.None);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _svc.CreateAsync(new CreateUserTemplateSubmissionRequest { TemplateVersionId = 1000, UserId = 1 }, CancellationToken.None);
        });
    }

    [Test]
    public async Task Update_Enforces_Concurrency_Token()
    {
        var created = await _svc.CreateAsync(new CreateUserTemplateSubmissionRequest { TemplateVersionId = 1000, UserId = 1 }, CancellationToken.None);
        var staleToken = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });

        // Using a wrong/stale RowVersion should throw
        Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
        {
            await _svc.UpdateAsync(created.Id, new UpdateUserTemplateSubmissionRequest
            {
                RowVersionBase64 = staleToken,
                SectionProgress = 50
            }, CancellationToken.None);
        });

        // Use the fresh token from the GET to succeed
        var fresh = await _svc.GetByIdAsync(created.Id, CancellationToken.None);
        var ok = await _svc.UpdateAsync(created.Id, new UpdateUserTemplateSubmissionRequest
        {
            RowVersionBase64 = fresh!.RowVersionBase64,
            SectionProgress = 50
        }, CancellationToken.None);

        Assert.That(ok.SectionProgress, Is.EqualTo(50));
    }

    [Test]
    public async Task SoftDelete_And_Restore_Works()
    {
        var created = await _svc.CreateAsync(new CreateUserTemplateSubmissionRequest { TemplateVersionId = 1000, UserId = 1 }, CancellationToken.None);
        var current = await _svc.GetByIdAsync(created.Id, CancellationToken.None);

        await _svc.SoftDeleteAsync(created.Id, current!.RowVersionBase64, CancellationToken.None);

        var afterDelete = await _svc.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.That(afterDelete, Is.Null);

        var soft = await _svc.GetSoftDeletedByIdAsync(created.Id, CancellationToken.None);
        Assert.That(soft, Is.Not.Null);

        await _svc.RestoreAsync(created.Id, soft!.RowVersionBase64, CancellationToken.None);

        var restored = await _svc.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.That(restored, Is.Not.Null);
    }

    [Test]
    public async Task Search_Paginates()
    {
        // Create 25 unique submissions by varying UserId (ensures unique (UserId, TemplateVersionId) pairs)
        for (int i = 0; i < 25; i++)
        {
            await _svc.CreateAsync(
                new CreateUserTemplateSubmissionRequest
                {
                    TemplateVersionId = 1000,
                    UserId = i + 1 // 1..25
                },
                CancellationToken.None);
        }

        var page1 = await _svc.SearchAsync(
            new() { Page = 1, PageSize = 10, SortBy = "createdAt", SortDir = "desc" },
            CancellationToken.None);

        var page3 = await _svc.SearchAsync(
            new() { Page = 3, PageSize = 10, SortBy = "createdAt", SortDir = "desc" },
            CancellationToken.None);

        Assert.That(page1.Items.Count, Is.EqualTo(10));
        Assert.That(page1.TotalCount, Is.EqualTo(25));
        Assert.That(page3.Items.Count, Is.EqualTo(5));
    }
}
