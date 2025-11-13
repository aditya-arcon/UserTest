// UserTest/RBAC/LegacyRoleMappingSeedTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace UserTest.RBAC;

[TestFixture]
public class LegacyRoleMappingSeedTests
{
    private ServiceProvider _sp = null!;
    private InMemoryDatabaseRoot _dbRoot = null!;
    private string _dbName = null!;

    [SetUp]
    public void SetUp()
    {
        // A SINGLE in-memory database per ServiceProvider:
        // - same _dbName for every DbContext created from this provider
        // - same _dbRoot to ensure all scopes share the same store
        _dbRoot = new InMemoryDatabaseRoot();
        _dbName = $"LegacyRoleMappingTests_{Guid.NewGuid()}";

        var services = new ServiceCollection();

        // In-memory configuration for RBAC mappings
        var configDict = new Dictionary<string, string?>
        {
            ["RBAC:LegacyRoleMappings:Admin:0"] = PermissionCodes.ManageUsersAndRoles,
            ["RBAC:LegacyRoleMappings:Admin:1"] = PermissionCodes.ViewRespondVerifs,
            ["RBAC:LegacyRoleMappings:User:0"] = PermissionCodes.ViewRespondVerifs
        };
        IConfiguration cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        services.AddSingleton(cfg);

        // IMPORTANT: use a FIXED db name + a SHARED root for this provider
        services.AddDbContext<ApplicationDbContext>(opt =>
            opt.UseInMemoryDatabase(_dbName, _dbRoot));

        _sp = services.BuildServiceProvider();

        // Ensure model is created in THIS database
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        if (_sp is not null)
        {
            _sp.Dispose();
        }
    }

    [Test]
    public async Task Applies_Admin_And_User_Mappings_From_Config()
    {
        await _sp.EnsureLegacyRolePermissionMappingsFromConfigAsync();

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Use IgnoreQueryFilters in case roles have global filters (tenancy/IsActive/soft-delete)
        var adminRole = await db.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.RoleName == RoleName.Admin);
        Assert.That(adminRole, Is.Not.Null, "Admin role should exist after applying legacy mappings.");
        var adminId = adminRole!.Id;

        var userRole = await db.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.RoleName == RoleName.User);
        Assert.That(userRole, Is.Not.Null, "User role should exist after applying legacy mappings.");
        var userId = userRole!.Id;

        var adminCodes = await db.RolePermissions
            .IgnoreQueryFilters()
            .Where(rp => rp.RoleId == adminId)
            .Join(db.Permissions.IgnoreQueryFilters(),
                  rp => rp.PermissionId,
                  p => p.Id,
                  (_, p) => p.Code)
            .OrderBy(c => c)
            .ToArrayAsync();

        var userCodes = await db.RolePermissions
            .IgnoreQueryFilters()
            .Where(rp => rp.RoleId == userId)
            .Join(db.Permissions.IgnoreQueryFilters(),
                  rp => rp.PermissionId,
                  p => p.Id,
                  (_, p) => p.Code)
            .OrderBy(c => c)
            .ToArrayAsync();

        CollectionAssert.AreEquivalent(
            new[] { PermissionCodes.ManageUsersAndRoles, PermissionCodes.ViewRespondVerifs },
            adminCodes);

        CollectionAssert.AreEquivalent(
            new[] { PermissionCodes.ViewRespondVerifs },
            userCodes);
    }

    [Test]
    public async Task Ignores_Unknown_Codes_And_Applies_Known_Ones()
    {
        // Build a NEW provider for this test case,
        // but still ensure every DbContext in that provider shares the same store.
        _sp.Dispose();

        var dbRoot2 = new InMemoryDatabaseRoot();
        var dbName2 = $"LegacyRoleMappingTests_{Guid.NewGuid()}";

        var services = new ServiceCollection();

        var configDict = new Dictionary<string, string?>
        {
            ["RBAC:LegacyRoleMappings:Admin:0"] = "Nope", // unknown permission code
            ["RBAC:LegacyRoleMappings:Admin:1"] = PermissionCodes.ViewRespondVerifs
        };
        IConfiguration cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        services.AddSingleton(cfg);

        services.AddDbContext<ApplicationDbContext>(opt =>
            opt.UseInMemoryDatabase(dbName2, dbRoot2));

        _sp = services.BuildServiceProvider();

        // Ensure schema for this provider's database
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        }

        // Apply legacy mappings (should ignore unknown "Nope" and keep known ones)
        await _sp.EnsureLegacyRolePermissionMappingsFromConfigAsync();

        using var verifyScope = _sp.CreateScope();
        var vdb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var adminRole = await vdb.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.RoleName == RoleName.Admin);
        Assert.That(adminRole, Is.Not.Null, "Admin role should exist after applying legacy mappings.");
        var adminId = adminRole!.Id;

        var adminCodes = await vdb.RolePermissions
            .IgnoreQueryFilters()
            .Where(rp => rp.RoleId == adminId)
            .Join(vdb.Permissions.IgnoreQueryFilters(),
                  rp => rp.PermissionId,
                  p => p.Id,
                  (_, p) => p.Code)
            .OrderBy(c => c)
            .ToArrayAsync();

        CollectionAssert.AreEquivalent(
            new[] { PermissionCodes.ViewRespondVerifs },
            adminCodes);
    }
}
