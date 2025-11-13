using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using IDV_Backend.Contracts.TemplatesLinkGenerations;
using IDV_Backend.Data;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Models.TemplatesLinkGenerations;
using IDV_Backend.Repositories.TemplatesLinkGenerations;
using IDV_Backend.Services.Security;
using IDV_Backend.Services.TemplatesLinkGenerations;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

// Minimal user entity alias used by your model mapping
using UserEntity = IDV_Backend.Models.User.User;

namespace UserTest.Services.TemplatesLinkGenerations
{
    public sealed class TemplatesLinkGenerationServiceTests
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

        private static async Task<UserEntity> SeedUserAsync(ApplicationDbContext db, long id = 1, string email = "u@test.com")
        {
            var u = new UserEntity
            {
                Id = id,
                FirstName = "Test",
                LastName = "User",
                Email = email,
                Phone = "1234"
            };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            return u;
        }

        private static async Task<IDV_Backend.Models.TemplateVersion.TemplateVersion> SeedActiveVersionAsync(
            ApplicationDbContext db, long versionId = 10)
        {
            var v = new IDV_Backend.Models.TemplateVersion.TemplateVersion
            {
                VersionId = versionId,
                Status = TemplateVersionStatus.Active,
                TemplateId = 99,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };
            db.TemplateVersions.Add(v);
            await db.SaveChangesAsync();
            return v;
        }

        private static ILinkTokenService NewFakeToken() => new FakeLinkTokenService();

        [Test]
        public async Task CreateAsync_Creates_New_Link_And_Returns_Response()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            await SeedUserAsync(db, 5, "a@b.com");
            await SeedActiveVersionAsync(db, 77);

            var repo = new TemplatesLinkGenerationRepository(db);
            var svc = new TemplatesLinkGenerationService(db, repo, NewFakeToken());

            var expires = DateTime.UtcNow.AddHours(2);
            var req = new CreateLinkRequest(UserId: 5, TemplateVersionId: 77, ExpiresAtUtc: expires);

            var resp = await svc.CreateAsync(req);

            resp.Should().NotBeNull();
            resp.UserId.Should().Be(5);
            resp.TemplateVersionId.Should().Be(77);
            resp.ShortCode.Should().NotBeNullOrWhiteSpace();
            resp.ExpiresAtUtc.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));

            var row = await db.TemplatesLinks.FirstOrDefaultAsync();
            row.Should().NotBeNull();
            row!.UserId.Should().Be(5);
            row.TemplateVersionId.Should().Be(77);
            row.ExpiresAt.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
            row.ShortCodeHash.Should().NotBeNullOrWhiteSpace();
        }

        [Test]
        public async Task CreateAsync_Refreshes_Existing_Link_For_Same_User_And_Version()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            await SeedUserAsync(db, 7, "c@d.com");
            await SeedActiveVersionAsync(db, 100);

            var repo = new TemplatesLinkGenerationRepository(db);
            var svc = new TemplatesLinkGenerationService(db, repo, NewFakeToken());

            var expires1 = DateTime.UtcNow.AddHours(1);
            var resp1 = await svc.CreateAsync(new CreateLinkRequest(7, 100, expires1));
            var row1 = await db.TemplatesLinks.AsNoTracking().FirstAsync();
            var hash1 = row1.ShortCodeHash;

            var expires2 = DateTime.UtcNow.AddHours(3);
            var resp2 = await svc.CreateAsync(new CreateLinkRequest(7, 100, expires2));
            var row2 = await db.TemplatesLinks.AsNoTracking().FirstAsync();

            // Same PK, new expiry, rotated token/hash
            row2.LinkId.Should().Be(row1.LinkId);
            row2.ExpiresAt.Should().BeCloseTo(expires2, TimeSpan.FromSeconds(1));
            row2.ShortCodeHash.Should().NotBeNullOrWhiteSpace();
            row2.ShortCodeHash.Should().NotBe(hash1);

            resp2.ShortCode.Should().NotBe(resp1.ShortCode);
        }

        [Test]
        public async Task ResolveAsync_Returns_Null_For_Invalid_Or_Expired()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            await SeedUserAsync(db, 1);
            await SeedActiveVersionAsync(db, 2);

            var repo = new TemplatesLinkGenerationRepository(db);
            var svc = new TemplatesLinkGenerationService(db, repo, NewFakeToken());

            // Invalid short code
            (await svc.ResolveAsync("", default)).Should().BeNull();
            (await svc.ResolveAsync("NOT_A_TOKEN", default)).Should().BeNull();

            // Create a valid link then manually expire it to test expiry block
            var resp = await svc.CreateAsync(new CreateLinkRequest(1, 2, DateTime.UtcNow.AddMinutes(5)));
            var row = await db.TemplatesLinks.FirstAsync();
            row.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();

            (await svc.ResolveAsync(resp.ShortCode, default)).Should().BeNull();
        }

        [Test]
        public async Task CleanupExpiredLinksAsync_Removes_Expired_Rows()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            await SeedUserAsync(db, 1);
            await SeedActiveVersionAsync(db, 10);

            // Seed 1 expired + 1 active directly (bypass service validation)
            db.TemplatesLinks.AddRange(
                new TemplatesLinkGeneration
                {
                    UserId = 1,
                    TemplateVersionId = 10,
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
                    ShortCodeHash = "EXPIRED"
                },
                new TemplatesLinkGeneration
                {
                    UserId = 1,
                    TemplateVersionId = 10,
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                    ShortCodeHash = "ACTIVE"
                }
            );
            await db.SaveChangesAsync();

            var repo = new TemplatesLinkGenerationRepository(db);
            var svc = new TemplatesLinkGenerationService(db, repo, NewFakeToken());

            var removed = await svc.CleanupExpiredLinksAsync();
            removed.Should().Be(1);

            var all = await db.TemplatesLinks.AsNoTracking().ToListAsync();
            all.Should().HaveCount(1);
            all[0].ShortCodeHash.Should().Be("ACTIVE");
        }

        // --------- Fake token service (deterministic, minimal) ----------
        private sealed class FakeLinkTokenService : ILinkTokenService
        {
            // Format: TLK|<userId>|<versionId>|<ticks>
            public string GenerateToken(LinkTokenPayload payload)
            {
                var ticks = payload.ExpiryAtUtc.Ticks;
                return $"TLK|{payload.UserId}|{payload.TemplateVersionId}|{ticks}";
            }

            public bool TryDecrypt(string token, out LinkTokenPayload? payload)
            {
                payload = null;
                if (string.IsNullOrWhiteSpace(token)) return false;
                var parts = token.Split('|');
                if (parts.Length != 4 || parts[0] != "TLK") return false;
                if (!long.TryParse(parts[1], out var userId)) return false;
                if (!long.TryParse(parts[2], out var versionId)) return false;
                if (!long.TryParse(parts[3], out var ticks)) return false;

                payload = new LinkTokenPayload(
                    UserId: userId,
                    TemplateVersionId: versionId,
                    FirstName: "N/A",
                    LastName: "N/A",
                    Email: "n/a@example.com",
                    CreatedAtUtc: DateTime.MinValue,
                    ExpiryAtUtc: new DateTime(ticks, DateTimeKind.Utc));
                return true;
            }
        }
    }
}
