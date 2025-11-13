// UserTest/Roles/RolesVersionServiceCachingTests.cs
using System.Threading.Tasks;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Repositories.SystemStateRepo;
using IDV_Backend.Services.Roles;
using IDV_Backend.Services.SystemStateSvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework;

namespace UserTest.Roles;

public class RolesVersionServiceCachingTests
{
    [Test]
    public async Task Get_Uses_Cache_And_Bump_Updates_Cache()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"roles_ver_cache_{TestContext.CurrentContext.Test.ID}")
            .Options;

        await using var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();

        if (!await db.SystemState.AnyAsync())
        {
            db.SystemState.Add(new SystemState { Id = 1, RolesVersion = 1 });
            await db.SaveChangesAsync();
        }

        var repo = new SystemStateRepository(db);
        var sysSvc = new SystemStateService(repo);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var rolesVerSvc = new RolesVersionService(sysSvc, cache);

        // First read -> caches 1
        var v1 = await rolesVerSvc.GetAsync();
        Assert.That(v1, Is.EqualTo(1));

        // Bump -> should set cache to 2
        var v2 = await rolesVerSvc.BumpAsync();
        Assert.That(v2, Is.EqualTo(2));

        // Another GetAsync -> should return cached 2 without DB call
        var v3 = await rolesVerSvc.GetAsync();
        Assert.That(v3, Is.EqualTo(2));
    }
}
