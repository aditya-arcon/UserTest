// UserTest/Roles/RolesVersionServiceSmokeTests.cs
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

public class RolesVersionServiceSmokeTests
{
    [Test]
    public async Task Get_Then_Bump_Works_EndToEnd()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"roles_ver_smoke_{TestContext.CurrentContext.Test.ID}")
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

        var svc = new RolesVersionService(sysSvc, cache);

        var v1 = await svc.GetAsync();
        Assert.That(v1, Is.EqualTo(1));

        var v2 = await svc.BumpAsync();
        Assert.That(v2, Is.EqualTo(2));

        var v3 = await svc.GetAsync();
        Assert.That(v3, Is.EqualTo(2));
    }
}
