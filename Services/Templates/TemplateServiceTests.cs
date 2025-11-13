using FluentAssertions;
using FluentValidation;
using IDV_Backend.Constants;
using IDV_Backend.Contracts.Template;
using IDV_Backend.Contracts.Template.Validators;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.Invitations;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Repositories.Templates;
using IDV_Backend.Services;
using IDV_Backend.Services.Audit;
using IDV_Backend.Services.Common;
using IDV_Backend.Services.Invitations;
using IDV_Backend.Services.TemplateServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IDV_Backend.Contracts.Invitations;
using IDV_Backend.Contracts.TemplatesLinkGenerations;


// Aliases
using UserEntity = IDV_Backend.Models.User.User;

namespace UserTest.Services.Templates
{
    public sealed class TemplateServiceTests
    {
        private static ApplicationDbContext NewDb(string name)
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name).EnableSensitiveDataLogging().Options;
            var db = new ApplicationDbContext(opts);
            db.Database.EnsureCreated();
            return db;
        }

        private static async Task<UserEntity> SeedUserAsync(ApplicationDbContext db, long id = 1, string email = "tester@example.com")
        {
            var u = new UserEntity { Id = id, FirstName = "Test", LastName = "User", Email = email };
            db.Users.Add(u); await db.SaveChangesAsync(); return u;
        }

        private static ITemplateService NewSvc(ApplicationDbContext db, ITemplateRepository repo)
        {
            var current = new FakeCurrentUser(1, "tester");
            var audit = new FakeAuditLogger();
            var createV = new TemplateCreateDtoValidator();
            var updateV = new TemplateUpdateDtoValidator();
            var invites = new FakeInvitationService();
            return new TemplateService(db, repo, audit, current, createV, updateV, invites);
        }

        [Test]
        public async Task CreateTemplateAsync_Creates_Version_And_Default_Sections()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            await SeedUserAsync(db, 1);

            var repo = new TemplateRepository(db);
            var svc = NewSvc(db, repo);

            var dto = new TemplateCreateDto { Name = "KYC", Description = "desc" };
            var created = await svc.CreateTemplateAsync(dto, currentUserId: 1, ct: default, mode: TemplateMode.Default);

            created.Should().NotBeNull();
            created.Name.Should().Be("KYC");
            created.Versions.Should().HaveCount(1);
            created.Versions[0].Status.Should().Be(nameof(TemplateVersionStatus.Draft));
            created.Versions[0].Sections.Any(s => s.SectionType == SectionTypes.Biometrics && s.IsActive).Should().BeFalse();
        }

        [Test]
        public async Task UpdateTemplateAsync_Enforces_Name_Uniqueness()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            await SeedUserAsync(db, 1);
            var repo = new TemplateRepository(db);
            var svc = NewSvc(db, repo);

            var a = await svc.CreateTemplateAsync(new TemplateCreateDto { Name = "Alpha" }, 1);
            var b = await svc.CreateTemplateAsync(new TemplateCreateDto { Name = "Beta" }, 1);

            Func<Task> clash = () => svc.UpdateTemplateAsync(b.Id, new TemplateUpdateDto { Name = "Alpha" }, 1);
            await clash.Should().ThrowAsync<InvalidOperationException>();
        }

        [Test]
        public async Task DeleteTemplateAsync_SoftDeletes_And_Avoids_Normalized_Collision()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            await SeedUserAsync(db, 1);
            var repo = new TemplateRepository(db);
            var svc = NewSvc(db, repo);

            var t1 = await svc.CreateTemplateAsync(new TemplateCreateDto { Name = "Dup" }, 1);
            var t2 = await svc.CreateTemplateAsync(new TemplateCreateDto { Name = "Other" }, 1); // unique enforced, this is allowed as separate creation? No, normalized clash blocked; so create different then rename.

            // Rename t2 to different then delete t1 to test normalized suffix
            await svc.UpdateTemplateAsync(t2.Id, new TemplateUpdateDto { Name = "Dup2" }, 1);

            var ok = await svc.DeleteTemplateAsync(t1.Id);
            ok.Should().BeTrue();

            var deleted = await db.Templates.IgnoreQueryFilters().FirstAsync(x => x.Id == t1.Id);
            deleted.IsDeleted.Should().BeTrue();
            deleted.NameNormalized.Should().StartWith("DUP");
        }

        [Test]
        public async Task DeactivateTemplateAsync_Deprecated_When_Invites_Exist()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            await SeedUserAsync(db, 1);
            var repo = new TemplateRepository(db);
            var svc = NewSvc(db, repo);

            var t = await svc.CreateTemplateAsync(new TemplateCreateDto { Name = "KYC" }, 1);
            // promote draft to active by hand (service here does not expose activate; replicate minimal)
            var v = await db.TemplateVersions.FirstAsync();
            v.Status = TemplateVersionStatus.Active;
            v.IsActive = true;
            await db.SaveChangesAsync();

            // seed link (future expiry)
            db.TemplatesLinks.Add(new IDV_Backend.Models.TemplatesLinkGenerations.TemplatesLinkGeneration
            {
                UserId = 1,
                TemplateVersionId = v.VersionId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                ShortCodeHash = "X"
            });
            await db.SaveChangesAsync();

            var result = await svc.DeactivateTemplateAsync(t.Id);
            result.Versions.First().Status.Should().Be(nameof(TemplateVersionStatus.Deprecated));
            result.Versions.First().IsActive.Should().BeFalse();
        }

        [Test]
        public async Task DeactivateTemplateAsync_Inactive_When_No_Invites()
        {
            var dbName = Guid.NewGuid().ToString("N");
            await using var db = NewDb(dbName);
            await SeedUserAsync(db, 1);
            var repo = new TemplateRepository(db);
            var svc = NewSvc(db, repo);

            var t = await svc.CreateTemplateAsync(new TemplateCreateDto { Name = "KYC2" }, 1);

            var v = await db.TemplateVersions.FirstAsync();
            v.Status = TemplateVersionStatus.Active;
            v.IsActive = true; await db.SaveChangesAsync();

            var result = await svc.DeactivateTemplateAsync(t.Id);
            result.Versions.First().Status.Should().Be(nameof(TemplateVersionStatus.Inactive));
        }

        // ------------ fakes ------------
        private sealed class FakeCurrentUser : ICurrentUser
        {
            public FakeCurrentUser(long id, string name) { UserId = id; UserName = name; }
            public long UserId { get; }
            public string? UserName { get; }
            public string? Email => "tester@example.com";
        }

        private sealed class FakeAuditLogger : ITemplateAuditLogger
        {
            public List<(string Action, string Details)> Events { get; } = new();
            public Task LogAsync(long templateId, long userId, string? userDisplayName, string action, string? details, System.Threading.CancellationToken ct = default)
            {
                Events.Add((action, details ?? "")); return Task.CompletedTask;
            }
        }

        private sealed class FakeInvitationService : IInvitationService
        {
            public Task<LinkResponse> SendAsync(SendInvitationRequest request, long createdBy, CancellationToken ct = default)
            {
                // Return a minimal, valid response object; values don’t matter to these tests
                var resp = new LinkResponse(
                    LinkId: 1,
                    UserId: 1,
                    ShortCode: "SC",
                    TemplateVersionId: request.VersionId,
                    User: null,
                    ExpiresAtUtc: DateTime.UtcNow.AddMinutes(5)
                );
                return Task.FromResult(resp);
            }

            public Task<IReadOnlyList<InviteeDto>> GetByTemplateIdAsync(long templateId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<InviteeDto>>(Array.Empty<InviteeDto>());

            public Task<IReadOnlyList<InviteeDto>> GetByVersionIdAsync(long versionId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<InviteeDto>>(Array.Empty<InviteeDto>());

            public Task<IDictionary<long, List<InviteeDto>>> GetByVersionIdsAsync(IReadOnlyList<long> versionIds, CancellationToken ct = default)
                => Task.FromResult<IDictionary<long, List<InviteeDto>>>(
                    versionIds.ToDictionary(id => id, _ => new List<InviteeDto>())
                );
        }

    }
}
