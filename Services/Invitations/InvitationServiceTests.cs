using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using IDV_Backend.Contracts.Invitations;
using IDV_Backend.Contracts.TemplatesLinkGenerations;
using IDV_Backend.Data;
using IDV_Backend.Models.User;                    // singular namespace for User
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Models.TemplatesLinkGenerations;
using IDV_Backend.Models.VerificationResults;
using IDV_Backend.Repositories.Invitations;
using IDV_Backend.Services.Invitations;
using IDV_Backend.Services.TemplatesLinkGenerations;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Services.Invitations
{
    public sealed class InvitationServiceTests
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

        // Simple "no-op" validator that implements the contract fully
        private sealed class PassThruValidator : AbstractValidator<SendInvitationRequest>
        {
            public PassThruValidator()
            {
                // Intentionally no rules for these tests
            }
        }

        // Fake link service that matches your ITemplatesLinkGenerationService precisely
        private sealed class FakeLinkService : ITemplatesLinkGenerationService
        {
            [Obsolete("Legacy overload not used in tests")]
            public Task<LinkResponse> CreateAsync(long userId, long versionId, CancellationToken ct = default)
                => Task.FromResult(new LinkResponse(
                    LinkId: 1,
                    TemplateVersionId: versionId,
                    ShortCode: "https://example/legacy",
                    UserId: userId,
                    User: null,           // avoid ctor/signature differences
                    ExpiresAtUtc: null      // tests don't depend on expiry
                    ));

            public Task<LinkResponse> CreateAsync(CreateLinkRequest request, CancellationToken ct = default)
                => Task.FromResult(new LinkResponse(
                    LinkId: 2,
                    TemplateVersionId: request.TemplateVersionId,  // NOTE: TemplateVersionId, not VersionId
                    ShortCode: "https://example/new",
                    UserId: request.UserId,
                    User: null,           // keep null to avoid constructor shape differences
                    ExpiresAtUtc: null      // CreateLinkRequest in your codebase has no ExpiresAt
                    ));

            public Task<LinkResponse?> ResolveAsync(string token, CancellationToken ct = default)
                => Task.FromResult<LinkResponse?>(null);

            public Task<int> CleanupExpiredLinksAsync(CancellationToken ct = default)
                => Task.FromResult(0);
        }

        private static async Task<long> SeedUserAsync(ApplicationDbContext db, long id, string first, string last, string email)
        {
            db.Users.Add(new User
            {
                Id = id,
                FirstName = first,
                LastName  = last,
                Email     = email
            });
            await db.SaveChangesAsync();
            return id;
        }

        private static async Task<long> SeedTemplateVersionAsync(ApplicationDbContext db, long templateId, long versionId)
        {
            db.TemplateVersions.Add(new TemplateVersion
            {
                TemplateId     = templateId,
                VersionId      = versionId,
                IsDeleted      = false,
                CreatedAt      = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return versionId;
        }

        private static async Task SeedLinkAsync(ApplicationDbContext db, long userId, long versionId, DateTime? expiresAt = null)
        {
            db.TemplatesLinks.Add(new TemplatesLinkGeneration
            {
                UserId            = userId,
                TemplateVersionId = versionId,
                // Do NOT set properties that don't exist in your model (e.g., Token, Id)
                ExpiresAt         = expiresAt,
                CreatedAt         = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        private static async Task<long> SeedVerifiedAsync(ApplicationDbContext db, long userId, long versionId)
        {
            // Create a submission & verification result that marks this (user,version) as verified
            var uts = new IDV_Backend.Models.UserTemplateSubmissions.UserTemplateSubmission
            {
                UserId            = userId,
                TemplateVersionId = versionId,
                Status            = IDV_Backend.Models.UserTemplateSubmissions.SubmissionStatus.Active, // avoid non-existent 'Completed'
                IsDeleted         = false
                // Do NOT set CreatedAt if the entity doesn't have it
            };
            db.UserTemplateSubmissions.Add(uts);
            await db.SaveChangesAsync();

            db.VerificationResults.Add(new VerificationResult
            {
                UserTemplateSubmissionId = uts.Id,
                ManualStatus             = ManualVerificationStatus.Verified,
                IsDeleted                = false,
                CreatedAt                = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            return uts.Id;
        }

        [Test]
        public async Task GetByVersionIdAsync_Maps_Rows_And_Verification_Flags()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);

            var versionId = await SeedTemplateVersionAsync(db, templateId: 10, versionId: 1001);
            var u1 = await SeedUserAsync(db, 2001, "Ada",   "Lovelace", "ada@example.com");
            var u2 = await SeedUserAsync(db, 2002, "Grace", "Hopper",   "grace@example.com");

            await SeedLinkAsync(db, u1, versionId);
            await SeedLinkAsync(db, u2, versionId);
            await SeedVerifiedAsync(db, u1, versionId); // only u1 verified

            var repo = new InvitationRepository(db);
            var svc  = new InvitationService(repo, new PassThruValidator(), new FakeLinkService());

            var list = await svc.GetByVersionIdAsync(versionId);

            list.Should().HaveCount(2);
            list.Single(x => x.Email == "ada@example.com").VerificationStatus.Should().Be("Verified");
            list.Single(x => x.Email == "grace@example.com").VerificationStatus.Should().Be("NotVerified");
        }

        [Test]
        public async Task GetByTemplateIdAsync_Aggregates_Across_Versions_And_Sorts_By_Name()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);

            var templateId = 77L;
            var v1 = await SeedTemplateVersionAsync(db, templateId, versionId: 1100);
            var v2 = await SeedTemplateVersionAsync(db, templateId, versionId: 1101);

            var u1 = await SeedUserAsync(db, 3001, "Zoe", "Zimmer", "zoe@example.com");
            var u2 = await SeedUserAsync(db, 3002, "Al",  "Alpha",  "al@example.com");

            await SeedLinkAsync(db, u1, v1, expiresAt: DateTime.UtcNow.AddDays(-1)); // expired
            await SeedLinkAsync(db, u2, v2, expiresAt: DateTime.UtcNow.AddDays( 2)); // active
            await SeedVerifiedAsync(db, u2, v2); // only u2 verified

            var repo = new InvitationRepository(db);
            var svc  = new InvitationService(repo, new PassThruValidator(), new FakeLinkService());

            var list = await svc.GetByTemplateIdAsync(templateId);

            list.Should().HaveCount(2);
            list.Should().BeInAscendingOrder(x => x.Name); // Al, then Zoe
            list.Single(x => x.Email == "al@example.com").VerificationStatus.Should().Be("Verified");
            list.Single(x => x.Email == "zoe@example.com").Status.Should().Be("EXPIRED");
        }

        [Test]
        public async Task GetByVersionIdsAsync_Groups_And_Sorts_Within_Each_List()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);

            var tId = 88L;
            var v1 = await SeedTemplateVersionAsync(db, tId, 2001);
            var v2 = await SeedTemplateVersionAsync(db, tId, 2002);

            var u1 = await SeedUserAsync(db, 4001, "B", "Bee", "b@example.com");
            var u2 = await SeedUserAsync(db, 4002, "A", "Aye", "a@example.com");
            var u3 = await SeedUserAsync(db, 4003, "C", "See", "c@example.com");

            await SeedLinkAsync(db, u1, v1);
            await SeedLinkAsync(db, u2, v1);
            await SeedLinkAsync(db, u3, v2);
            await SeedVerifiedAsync(db, u2, v1); // only u2 verified in v1

            var repo = new InvitationRepository(db);
            var svc  = new InvitationService(repo, new PassThruValidator(), new FakeLinkService());

            var dict = await svc.GetByVersionIdsAsync(new[] { v1, v2 });

            dict.Should().ContainKeys(v1, v2);
            dict[v1].Select(x => x.Email).Should().Contain(new[] { "a@example.com", "b@example.com" });
            dict[v1].Should().BeInAscendingOrder(x => x.Name); // A then B
            dict[v1].Single(x => x.Email == "a@example.com").VerificationStatus.Should().Be("Verified");
            dict[v2].Single().Email.Should().Be("c@example.com");
        }
    }
}
