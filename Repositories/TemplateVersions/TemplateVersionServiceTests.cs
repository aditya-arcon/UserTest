using FluentAssertions;
using FluentValidation;
using IDV_Backend.Constants;
using IDV_Backend.Contracts.Invitations;
using IDV_Backend.Contracts.TemplatesLinkGenerations;
using IDV_Backend.Contracts.TemplateVersions;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.TemplatesLinkGenerations;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Repositories.TemplateVersions; // ✅ repo
using IDV_Backend.Services.Audit;
using IDV_Backend.Services.Common;
using IDV_Backend.Services.Invitations;
using IDV_Backend.Services.TemplateVersion;
using IDV_Backend.Services.TemplatesLinkGenerations; // ✅ link service interface
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
// Aliases to match your model mapping
using UserEntity = IDV_Backend.Models.User.User;

namespace UserTest.Services.TemplateVersions
{
    public sealed class TemplateVersionServiceTests
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

        private static async Task<UserEntity> SeedUserAsync(ApplicationDbContext db, long id = 1, string email = "tester@example.com")
        {
            var u = new UserEntity
            {
                Id = id,
                FirstName = "Test",
                LastName = "User",
                Email = email
            };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            return u;
        }

        private static async Task<Template> SeedTemplateAsync(ApplicationDbContext db, long id = 11, string name = "Onboarding")
        {
            var t = new Template
            {
                Id = id,
                Name = name,
                NameNormalized = name.ToUpperInvariant(),
                Mode = TemplateMode.Default,
                CreatedBy = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                IsDeleted = false
            };
            db.Templates.Add(t);
            await db.SaveChangesAsync();
            return t;
        }

        private static ITemplateVersionService NewSvc(
            ApplicationDbContext db,
            long currentUserId = 1,
            string currentUserName = "tester")
        {
            var current = new FakeCurrentUser(currentUserId, currentUserName);
            var audit = new FakeAuditLogger();
            var createV = new IDV_Backend.Contracts.TemplateVersion.Validators.CreateTemplateVersionRequestValidator(db);
            var updateV = new IDV_Backend.Contracts.TemplateVersion.Validators.UpdateTemplateVersionRequestValidator();
            var inviteSvc = new FakeInvitationService();
            var linkSvc = new FakeNoopLinkService();
            var repo = new TemplateVersionRepository(db); // ✅ repository
            var logger = new NullLogger<TemplateVersionService>();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

            return new TemplateVersionService(
                db,
                current,
                audit,
                createV,
                updateV,
                inviteSvc,
                linkSvc,
                repo,          // ✅ pass repo in correct position
                logger,
                config);
        }

        [Test]
        public async Task CreateAsync_Starts_As_Draft_And_Creates_Default_Sections()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);

            await SeedUserAsync(db, 1);
            await SeedTemplateAsync(db, 101, "KYC");

            var svc = NewSvc(db, 1, "creator");

            var created = await svc.CreateAsync(new CreateTemplateVersionRequest
            {
                TemplateId = 101,
                VersionName = "V1",
                EnforceRekyc = false,
                ChangeSummary = "Initial version"
            }, CancellationToken.None, TemplateMode.Default);

            created.Should().NotBeNull();
            created.TemplateId.Should().Be(101);
            created.Status.Should().Be(nameof(TemplateVersionStatus.Draft));
            created.IsActive.Should().BeFalse();
            created.VersionNumber.Should().Be(1);
            created.VersionName.Should().Be("V1");

            // Default/Standard: Personal + Documents active, Biometrics inactive
            created.Sections.Should().NotBeNull();
            created.Sections.Count.Should().BeGreaterThanOrEqualTo(2); // ✅ correct matcher
            created.Sections.Any(s => s.Name.Contains("Personal")).Should().BeTrue();
            created.Sections.Any(s => s.Name.Contains("Document")).Should().BeTrue();
        }

        [Test]
        public async Task ActivateAsync_Makes_This_Active_And_Deactivates_Others()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);

            await SeedUserAsync(db, 1);
            await SeedTemplateAsync(db, 200, "KYC");

            var svc = NewSvc(db);

            // Create two drafts
            var v1 = await svc.CreateAsync(new CreateTemplateVersionRequest { TemplateId = 200, VersionName = "V1" }, default);
            var v2 = await svc.CreateAsync(new CreateTemplateVersionRequest { TemplateId = 200, VersionName = "V2" }, default);

            // Activate v1 first
            var a1 = await svc.ActivateAsync(v1.VersionId, default);
            a1.IsActive.Should().BeTrue();
            a1.Status.Should().Be(nameof(TemplateVersionStatus.Active));

            // Activate v2; v1 should become inactive
            var a2 = await svc.ActivateAsync(v2.VersionId, default);
            a2.IsActive.Should().BeTrue();
            a2.Status.Should().Be(nameof(TemplateVersionStatus.Active));

            var rows = await db.TemplateVersions.AsNoTracking().Where(x => x.TemplateId == 200).ToListAsync();
            rows.Single(x => x.VersionId == v2.VersionId).IsActive.Should().BeTrue();
            rows.Single(x => x.VersionId == v1.VersionId).IsActive.Should().BeFalse();
            rows.Single(x => x.VersionId == v1.VersionId).Status.Should().Be(TemplateVersionStatus.Inactive);
        }

        [Test]
        public async Task DeactivateAsync_LatestActive_WithOutstandingLinks_Goes_Deprecated()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);

            await SeedUserAsync(db, 1);
            await SeedTemplateAsync(db, 300, "KYC");

            var svc = NewSvc(db);

            var created = await svc.CreateAsync(new CreateTemplateVersionRequest { TemplateId = 300, VersionName = "V1" }, default);
            var active = await svc.ActivateAsync(created.VersionId, default);

            // Seed one active link (future expiry)
            db.TemplatesLinks.Add(new TemplatesLinkGeneration
            {
                UserId = 1,
                TemplateVersionId = active.VersionId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                ShortCodeHash = "SC_HASH_1"
            });
            await db.SaveChangesAsync();

            var after = await svc.DeactivateAsync(active.VersionId, default);

            after.IsActive.Should().BeFalse();
            after.Status.Should().Be(nameof(TemplateVersionStatus.Deprecated));
        }

        [Test]
        public async Task DeactivateAsync_LatestActive_WithoutInvites_Goes_Inactive()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);

            await SeedUserAsync(db, 1);
            await SeedTemplateAsync(db, 301, "KYC");

            var svc = NewSvc(db);

            var created = await svc.CreateAsync(new CreateTemplateVersionRequest { TemplateId = 301, VersionName = "V1" }, default);
            var active = await svc.ActivateAsync(created.VersionId, default);

            var after = await svc.DeactivateAsync(active.VersionId, default);

            after.IsActive.Should().BeFalse();
            after.Status.Should().Be(nameof(TemplateVersionStatus.Inactive));
        }

        [Test]
        public async Task PatchAsync_OnDraft_Updates_InPlace()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);

            await SeedUserAsync(db, 1);
            await SeedTemplateAsync(db, 400, "KYC");

            var svc = NewSvc(db);
            var created = await svc.CreateAsync(new CreateTemplateVersionRequest
            {
                TemplateId = 400,
                VersionName = "V1",
                EnforceRekyc = false
            }, default);

            var patched = await svc.PatchAsync(created.VersionId, new UpdateTemplateVersionRequest
            {
                RowVersionBase64 = created.RowVersionBase64,
                EnforceRekyc = true,
                RekycDeadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
                ChangeSummary = "Enable reKYC"
            }, default);

            patched.EnforceRekyc.Should().BeTrue();
            patched.ChangeSummary.Should().Be("Enable reKYC");
            patched.VersionName.Should().Be("V1"); // unchanged
            patched.VersionNumber.Should().Be(1);  // in-place
        }

        [Test]
        public async Task PatchAsync_OnActive_AutoForks_New_Draft()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);

            await SeedUserAsync(db, 1);
            await SeedTemplateAsync(db, 500, "KYC");

            var svc = NewSvc(db);

            var created = await svc.CreateAsync(new CreateTemplateVersionRequest
            {
                TemplateId = 500,
                VersionName = "V1"
            }, default);

            var active = await svc.ActivateAsync(created.VersionId, default);

            var forked = await svc.PatchAsync(active.VersionId, new UpdateTemplateVersionRequest
            {
                RowVersionBase64 = active.RowVersionBase64,
                ChangeSummary = "Forked edits",
                VersionName = "V2"
            }, default);

            forked.VersionId.Should().NotBe(active.VersionId);
            forked.VersionNumber.Should().Be(2);
            forked.Status.Should().Be(nameof(TemplateVersionStatus.Draft));
            forked.VersionName.Should().Be("V2");
        }

        [Test]
        public async Task UpdateAsync_Status_Transition_Rules_Enforced()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);

            await SeedUserAsync(db, 1);
            await SeedTemplateAsync(db, 600, "KYC");

            var svc = NewSvc(db);

            var v = await svc.CreateAsync(new CreateTemplateVersionRequest
            {
                TemplateId = 600
            }, default);

            // Draft -> Active (allowed)
            var toActive = await svc.UpdateAsync(v.VersionId, new UpdateTemplateVersionRequest
            {
                RowVersionBase64 = v.RowVersionBase64,
                Status = "Active"
            }, default);
            toActive.Status.Should().Be(nameof(TemplateVersionStatus.Active));
            toActive.IsActive.Should().BeTrue();

            // Active -> Draft (NOT allowed)
            Func<Task> bad = () => svc.UpdateAsync(v.VersionId, new UpdateTemplateVersionRequest
            {
                RowVersionBase64 = toActive.RowVersionBase64,
                Status = "Draft"
            }, default);
            await bad.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Illegal status transition*");
        }

        // ------------------------- FAKES -------------------------

        private sealed class FakeCurrentUser : ICurrentUser
        {
            public FakeCurrentUser(long id, string name)
            {
                UserId = id;
                UserName = name;
            }
            public long UserId { get; }
            public string UserName { get; }
            public string? Email => "tester@example.com";
            public string? Role => "Admin";
        }

        private sealed class FakeAuditLogger : ITemplateAuditLogger
        {
            public Task LogAsync(long templateId, long userId, string? userDisplayName, string action, string? details, CancellationToken ct = default)
                => Task.CompletedTask;
        }


        private sealed class FakeInvitationService : IInvitationService
        {
            public Task<LinkResponse> SendAsync(SendInvitationRequest request, long createdBy, CancellationToken ct = default)
                => Task.FromResult(new LinkResponse(1, 1, "SC", 1, null, DateTime.UtcNow.AddHours(1)));

            public Task<IReadOnlyList<InviteeDto>> GetByTemplateIdAsync(long templateId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<InviteeDto>>(Array.Empty<InviteeDto>());

            public Task<IReadOnlyList<InviteeDto>> GetByVersionIdAsync(long versionId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<InviteeDto>>(Array.Empty<InviteeDto>());

            public Task<IDictionary<long, List<InviteeDto>>> GetByVersionIdsAsync(IReadOnlyList<long> versionIds, CancellationToken ct = default)
                => Task.FromResult<IDictionary<long, List<InviteeDto>>>(new Dictionary<long, List<InviteeDto>>());
        }

        // ✅ Implements the correct interface from IDV_Backend.Services.TemplatesLinkGenerations
        private sealed class FakeNoopLinkService : ITemplatesLinkGenerationService
        {
            [Obsolete("legacy")]
            public Task<IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse> CreateAsync(long userId, long templateVersionId, CancellationToken ct = default)
                => throw new NotImplementedException();

            public Task<IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse> CreateAsync(IDV_Backend.Contracts.TemplatesLinkGenerations.CreateLinkRequest request, CancellationToken ct = default)
                => throw new NotImplementedException();

            public Task<IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse?> ResolveAsync(string shortCode, CancellationToken ct = default)
                => Task.FromResult<IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse?>(null);

            public Task<int> CleanupExpiredLinksAsync(CancellationToken ct = default)
                => Task.FromResult(0);
        }
    }
}
