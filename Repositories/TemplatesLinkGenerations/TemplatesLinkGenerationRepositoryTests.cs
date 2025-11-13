using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IDV_Backend.Data;
using IDV_Backend.Models.TemplatesLinkGenerations;
using IDV_Backend.Repositories.TemplatesLinkGenerations;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.TemplatesLinkGenerations
{
    public sealed class TemplatesLinkGenerationRepositoryTests
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
        public async Task GetByUserAndVersion_Works()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            var repo = new TemplatesLinkGenerationRepository(db);

            var row = new TemplatesLinkGeneration
            {
                UserId = 5,
                TemplateVersionId = 9,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                ShortCodeHash = "ABC"
            };
            await repo.AddAsync(row);

            var found = await repo.GetByUserAndVersionAsync(5, 9);
            found.Should().NotBeNull();
            found!.ShortCodeHash.Should().Be("ABC");
        }

        [Test]
        public async Task GetByShortCodeHash_Works()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            var repo = new TemplatesLinkGenerationRepository(db);

            await repo.AddAsync(new TemplatesLinkGeneration
            {
                UserId = 1,
                TemplateVersionId = 2,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(2),
                ShortCodeHash = "HASH123"
            });

            var row = await repo.GetByShortCodeHashAsync("HASH123");
            row.Should().NotBeNull();
            row!.TemplateVersionId.Should().Be(2);
        }

        [Test]
        public async Task RemoveExpired_Removes_Only_Expired()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            var repo = new TemplatesLinkGenerationRepository(db);

            await repo.AddAsync(new TemplatesLinkGeneration
            {
                UserId = 1,
                TemplateVersionId = 2,
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
                ShortCodeHash = "EX1"
            });
            await repo.AddAsync(new TemplatesLinkGeneration
            {
                UserId = 1,
                TemplateVersionId = 2,
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                ShortCodeHash = "OK1"
            });

            var removed = await repo.RemoveExpiredAsync(DateTime.UtcNow);
            removed.Should().Be(1);

            var hashes = await db.TemplatesLinks.Select(x => x.ShortCodeHash).ToListAsync();
            hashes.Should().Contain("OK1").And.NotContain("EX1");
        }
    }
}
