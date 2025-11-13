using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Repositories.SystemStateRepo;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.SystemStateRepoTests;

public class SystemStateRepositoryTests
{
    private ApplicationDbContext _db = null!;
    private ISystemStateRepository _repo = null!;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"sysstate_repo_{TestContext.CurrentContext.Test.ID}")
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();

        // Apply config seeding behavior manually for InMemory:
        if (!_db.SystemState.AnyAsync().Result)
        {
            _db.SystemState.Add(new SystemState { Id = 1, RolesVersion = 1 });
            _db.SaveChanges();
        }

        _repo = new SystemStateRepository(_db);
    }

    [TearDown]
    public void Teardown() => _db.Dispose();

    [Test]
    public async Task EnsureExists_CreatesRow_WhenMissing()
    {
        // arrange: delete row
        _db.SystemState.RemoveRange(_db.SystemState);
        await _db.SaveChangesAsync();

        // act
        await _repo.EnsureExistsAsync(CancellationToken.None);

        // assert
        var row = await _repo.GetNoTrackingAsync();
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Id, Is.EqualTo(1));
        Assert.That(row.RolesVersion, Is.EqualTo(1));
    }

    [Test]
    public async Task GetTracked_ThenIncrement_Persists()
    {
        var tracked = await _repo.GetTrackedAsync();
        Assert.That(tracked, Is.Not.Null);

        tracked!.RolesVersion += 1;
        await _repo.SaveChangesAsync();

        var after = await _repo.GetNoTrackingAsync();
        Assert.That(after!.RolesVersion, Is.EqualTo(2));
    }
}
