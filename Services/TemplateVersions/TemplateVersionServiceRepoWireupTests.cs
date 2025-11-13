using FluentAssertions;
using IDV_Backend.Contracts.TemplateVersions;
using IDV_Backend.Data;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Repositories.TemplateVersions;
using IDV_Backend.Services.Audit;
using IDV_Backend.Services.Common;
using IDV_Backend.Services.Invitations;
using IDV_Backend.Services.TemplatesLinkGenerations;
using IDV_Backend.Services.TemplateVersion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using UserEntity = IDV_Backend.Models.User.User;

namespace UserTest.Services.TemplateVersions
{
    public sealed class TemplateVersionServiceRepoWireupTests
    {
        private static ApplicationDbContext NewDb(string name)
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name).EnableSensitiveDataLogging().Options;
            var db = new ApplicationDbContext(opts);
            db.Database.EnsureCreated();
            return db;
        }

        private static async Task SeedUserAndTemplateAsync(ApplicationDbContext db)
        {
            db.Users.Add(new UserEntity { Id = 1, FirstName = "T", LastName = "U", Email = "t@u" });
            await db.SaveChangesAsync();
            db.Templates.Add(new IDV_Backend.Models.Template { Id = 100, Name = "T", NameNormalized = "T", CreatedBy = 1, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        private static TemplateVersionService NewSvc(ApplicationDbContext db)
        {
            var current = new FakeCurrentUser(1, "tester");
            var audit = new FakeAuditLogger();
            var createV = new IDV_Backend.Contracts.TemplateVersion.Validators.CreateTemplateVersionRequestValidator(db);
            var updateV = new IDV_Backend.Contracts.TemplateVersion.Validators.UpdateTemplateVersionRequestValidator();
            var invites = new FakeInvitationService();
            var repo = new TemplateVersionRepository(db);
            var logger = new NullLogger<TemplateVersionService>();
            var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

            // linkService is not used in tests, but pass a stub to satisfy ctor
            var linkService = new FakeLinkService();

            return new TemplateVersionService(db, current, audit, createV, updateV, invites, linkService, repo, logger, config);
        }

        [Test]
        public async Task Create_Activate_Deactivate_Pipeline_Works_With_Repo()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            await SeedUserAndTemplateAsync(db);

            var svc = NewSvc(db);

            var created = await svc.CreateAsync(new CreateTemplateVersionRequest { TemplateId = 100, VersionName = "V1" }, CancellationToken.None);
            created.Status.Should().Be(nameof(TemplateVersionStatus.Draft));
            created.VersionNumber.Should().Be(1);

            var active = await svc.ActivateAsync(created.VersionId, CancellationToken.None);
            active.Status.Should().Be(nameof(TemplateVersionStatus.Active));
            active.IsActive.Should().BeTrue();

            var deactivated = await svc.DeactivateAsync(active.VersionId, CancellationToken.None);
            deactivated.Status.Should().BeOneOf(nameof(TemplateVersionStatus.Inactive), nameof(TemplateVersionStatus.Deprecated));
            deactivated.IsActive.Should().BeFalse();
        }

        // --------- Fakes ----------
        private sealed class FakeCurrentUser : ICurrentUser
        {
            public FakeCurrentUser(long id, string name) { UserId = id; UserName = name; }
            public long UserId { get; }
            public string? UserName { get; }
            public string? Email => "tester@example.com";
        }
        private sealed class FakeAuditLogger : ITemplateAuditLogger
        {
            public Task LogAsync(long templateId, long userId, string? userDisplayName, string action, string? details, CancellationToken ct = default)
                => Task.CompletedTask;
        }
        private sealed class FakeInvitationService : IInvitationService
        {
            public Task<IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse> SendAsync(IDV_Backend.Contracts.Invitations.SendInvitationRequest request, long createdBy, CancellationToken ct = default)
                => Task.FromResult(new IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse(1, 1, "SC", request.VersionId, null, DateTime.UtcNow.AddMinutes(5)));
            public Task<IReadOnlyList<IDV_Backend.Contracts.Invitations.InviteeDto>> GetByTemplateIdAsync(long templateId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<IDV_Backend.Contracts.Invitations.InviteeDto>>(Array.Empty<IDV_Backend.Contracts.Invitations.InviteeDto>());
            public Task<IReadOnlyList<IDV_Backend.Contracts.Invitations.InviteeDto>> GetByVersionIdAsync(long versionId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<IDV_Backend.Contracts.Invitations.InviteeDto>>(Array.Empty<IDV_Backend.Contracts.Invitations.InviteeDto>());
            public Task<IDictionary<long, System.Collections.Generic.List<IDV_Backend.Contracts.Invitations.InviteeDto>>> GetByVersionIdsAsync(System.Collections.Generic.IReadOnlyList<long> versionIds, CancellationToken ct = default)
                => Task.FromResult<IDictionary<long, System.Collections.Generic.List<IDV_Backend.Contracts.Invitations.InviteeDto>>>(versionIds.ToDictionary(id => id, _ => new System.Collections.Generic.List<IDV_Backend.Contracts.Invitations.InviteeDto>()));
        }
        // Add near the other using lines if you prefer shorter names:
        // using IDV_Backend.Contracts.TemplatesLinkGenerations;

        private sealed class FakeLinkService : ITemplatesLinkGenerationService
        {
            public Task<int> CleanupExpiredLinksAsync(CancellationToken ct = default)
                => Task.FromResult(0);

            public Task<IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse> CreateAsync(
                IDV_Backend.Contracts.TemplatesLinkGenerations.CreateLinkRequest request,
                CancellationToken ct = default)
            {
                var resp = new IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse(
                    LinkId: 1,
                    UserId: request.UserId,
                    ShortCode: "SC",
                    TemplateVersionId: request.TemplateVersionId,
                    User: null,
                    ExpiresAtUtc: request.ExpiresAtUtc,
                    EmailTemplateId: request.EmailTemplateId
                );
                return Task.FromResult(resp);
            }

            public Task<IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse> CreateAsync(
                long userId,
                long templateVersionId,
                CancellationToken ct = default)
            {
                var resp = new IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse(
                    LinkId: 2,
                    UserId: userId,
                    ShortCode: "SC2",
                    TemplateVersionId: templateVersionId,
                    User: null,
                    ExpiresAtUtc: DateTime.UtcNow.AddDays(7),
                    EmailTemplateId: null
                );
                return Task.FromResult(resp);
            }

            public Task<IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse?> ResolveAsync(
                string shortCode,
                CancellationToken ct = default)
            {
                // Minimal fake: always return a dummy link pointing to an arbitrary version
                var resp = new IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse(
                    LinkId: 3,
                    UserId: 1,
                    ShortCode: shortCode,
                    TemplateVersionId: 100,
                    User: null,
                    ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30),
                    EmailTemplateId: null
                );
                return Task.FromResult<IDV_Backend.Contracts.TemplatesLinkGenerations.LinkResponse?>(resp);
            }
        }

    }
}
