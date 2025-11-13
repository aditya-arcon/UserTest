using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.Roles;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UserTest.Repositories.Seeds
{
    [TestFixture]
    public class SeedMatrixTests
    {
        private static ApplicationDbContext NewContext()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"seed_matrix_{Guid.NewGuid()}")
                .Options;

            var db = new ApplicationDbContext(opts);
            // IMPORTANT: ensures HasData() runs for InMemory provider
            db.Database.EnsureCreated();
            return db;
        }

        private static async Task<string[]> GetCodesForRoleAsync(ApplicationDbContext db, RoleName role)
        {
            var roleId = await db.Roles
                .Where(r => r.RoleName == role)
                .Select(r => r.Id)
                .SingleAsync();

            var codes = await db.RolePermissions
                .Where(rp => rp.RoleId == roleId)
                .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
                .OrderBy(c => c)
                .ToArrayAsync();

            return codes;
        }

        [Test]
        public async Task PermissionCatalog_Count_IsNine()
        {
            await using var db = NewContext();
            var count = await db.Permissions.CountAsync();
            Assert.That(count, Is.EqualTo(9));
            // spot check a known code exists
            Assert.That(await db.Permissions.AnyAsync(p => p.Code == PermissionCodes.ManageUsersAndRoles), Is.True);
        }

        [Test]
        public async Task Roles_Count_IsNine()
        {
            await using var db = NewContext();
            var count = await db.Roles.CountAsync();
            Assert.That(count, Is.EqualTo(9));
            // spot check a known role exists
            Assert.That(await db.Roles.AnyAsync(r => r.RoleName == RoleName.SupportAdmin), Is.True);
        }

        [Test]
        public async Task SuperAdmin_Has_All_Nine_Permissions()
        {
            await using var db = NewContext();

            var superCodes = await GetCodesForRoleAsync(db, RoleName.SuperAdmin);
            Assert.That(superCodes.Length, Is.EqualTo(9));
            CollectionAssert.AreEquivalent(PermissionCodes.All, superCodes);
        }

        [Test]
        public async Task WorkflowAdmin_Has_CreateEditWorkflows_And_ViewRespondVerifs()
        {
            await using var db = NewContext();

            var codes = await GetCodesForRoleAsync(db, RoleName.WorkflowAdmin);
            CollectionAssert.AreEquivalent(new[]
            {
                PermissionCodes.CreateEditWorkflows,
                PermissionCodes.ViewRespondVerifs
            }, codes);
        }

        [Test]
        public async Task ComplianceOfficer_Has_ViewRespondVerifs_ManualOverride_AccessSensitiveData()
        {
            await using var db = NewContext();

            var codes = await GetCodesForRoleAsync(db, RoleName.ComplianceOfficer);
            CollectionAssert.AreEquivalent(new[]
            {
                PermissionCodes.ViewRespondVerifs,
                PermissionCodes.ManualOverrideReview,
                PermissionCodes.AccessSensitiveData
            }, codes);
        }

        [Test]
        public async Task VerificationAgent_Has_ViewRespondVerifs_And_ManualOverride()
        {
            await using var db = NewContext();

            var codes = await GetCodesForRoleAsync(db, RoleName.VerificationAgent);
            CollectionAssert.AreEquivalent(new[]
            {
                PermissionCodes.ViewRespondVerifs,
                PermissionCodes.ManualOverrideReview
            }, codes);
        }

        [Test]
        public async Task SupportAdmin_Has_ViewRespondVerifs_ManualOverride_ManageSupportTickets()
        {
            await using var db = NewContext();

            var codes = await GetCodesForRoleAsync(db, RoleName.SupportAdmin);
            CollectionAssert.AreEquivalent(new[]
            {
                PermissionCodes.ViewRespondVerifs,
                PermissionCodes.ManualOverrideReview,
                PermissionCodes.ManageSupportTickets
            }, codes);
        }

        [Test]
        public async Task ReadOnlyAuditor_Has_ViewRespondVerifs_Only()
        {
            await using var db = NewContext();

            var codes = await GetCodesForRoleAsync(db, RoleName.ReadOnlyAuditor);
            CollectionAssert.AreEquivalent(new[]
            {
                PermissionCodes.ViewRespondVerifs
            }, codes);
        }

        [Test]
        public async Task IntegrationAdmin_Has_ApiIntegrationMgmt_And_EditSystemSettings()
        {
            await using var db = NewContext();

            var codes = await GetCodesForRoleAsync(db, RoleName.IntegrationAdmin);
            CollectionAssert.AreEquivalent(new[]
            {
                PermissionCodes.ApiIntegrationMgmt,
                PermissionCodes.EditSystemSettings
            }, codes);
        }
    }
}
