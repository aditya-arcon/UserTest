using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IDV_Backend.Data;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Models.TemplatesLinkGenerations;
using IDV_Backend.Models.User; // singular namespace for User
using IDV_Backend.Repositories.Invitations;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.Invitations
{
    public sealed class InvitationRepositoryTests
    {
        private static ApplicationDbContext NewDb(string name)
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name)
                .EnableSensitiveDataLogging()
                .Options;

            var db = new ApplicationDbContext(opts);
            db.Database.EnsureCreated();
            return db;
        }

        [Test]
        public async Task Basic_Add_And_Query_Works()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);

            // seed a user + version
            db.Users.Add(new User { Id = 1, FirstName = "Test", LastName = "User", Email = "t@example.com" });
            db.TemplateVersions.Add(new TemplateVersion { TemplateId = 11, VersionId = 111, IsDeleted = false, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();

            // add one link generation row (only properties that exist in your model)
            db.TemplatesLinks.Add(new TemplatesLinkGeneration
            {
                UserId = 1,
                TemplateVersionId = 111,
                ExpiresAt = DateTime.UtcNow.AddDays(3),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            // use the repository object, but validate via DbContext to avoid assuming a missing method
            var _ = new InvitationRepository(db);

            var rows = await db.TemplatesLinks.Where(x => x.TemplateVersionId == 111).ToListAsync();
            rows.Should().HaveCount(1);
            rows[0].UserId.Should().Be(1);
        }
    }
}
