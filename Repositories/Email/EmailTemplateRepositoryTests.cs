using IDV_Backend.Data;
using IDV_Backend.Models.Email;
using IDV_Backend.Repositories.Email;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.Email
{
    public sealed class EmailTemplateRepositoryTests
    {
        private ApplicationDbContext _ctx = default!;
        private EmailTemplateRepository _repo = default!;

        [SetUp]
        public void Setup()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableSensitiveDataLogging()
                .Options;

            _ctx = new ApplicationDbContext(opts);
            _ctx.Database.EnsureCreated();

            _repo = new EmailTemplateRepository(_ctx);
        }

        [TearDown]
        public void Teardown()
        {
            _ctx?.Dispose();
        }

        [Test]
        public async Task GetByIdAsync_Returns_Template_WhenExists()
        {
            var tpl = new EmailTemplate
            {
                Id = 1001,
                Key = "InviteEmail",
                ScopeType = "Global",
                IsDefault = true,
                SubjectTemplate = "Subj {{FirstName}}",
                BodyTemplate = "Body",
                IsHtml = false
            };
            _ctx.EmailTemplates.Add(tpl);
            await _ctx.SaveChangesAsync();

            var found = await _repo.GetByIdAsync(1001);
            Assert.That(found, Is.Not.Null);
            Assert.That(found!.Key, Is.EqualTo("InviteEmail"));
        }

        [Test]
        public async Task ResolveInviteTemplateAsync_Prefers_TemplateVersion_NonDefault()
        {
            var tvId = 55L;

            _ctx.EmailTemplates.AddRange(
                new EmailTemplate
                {
                    Id = 1,
                    Key = "InviteEmail",
                    ScopeType = "Global",
                    IsDefault = true,
                    SubjectTemplate = "Global Default",
                    BodyTemplate = "Global Body",
                    IsHtml = false
                },
                new EmailTemplate
                {
                    Id = 2,
                    Key = "InviteEmail",
                    ScopeType = "TemplateVersion",
                    ScopeId = tvId,
                    IsDefault = false,
                    SubjectTemplate = "TV Specific",
                    BodyTemplate = "Body TV",
                    IsHtml = true
                }
            );
            await _ctx.SaveChangesAsync();

            var resolved = await _repo.ResolveInviteTemplateAsync(tvId);
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved!.Id, Is.EqualTo(2));
            Assert.That(resolved.SubjectTemplate, Is.EqualTo("TV Specific"));
        }

        [Test]
        public async Task ResolveInviteTemplateAsync_FallsBack_To_GlobalDefault()
        {
            _ctx.EmailTemplates.Add(
                new EmailTemplate
                {
                    Id = 3,
                    Key = "InviteEmail",
                    ScopeType = "Global",
                    IsDefault = true,
                    SubjectTemplate = "Global Default",
                    BodyTemplate = "Body G",
                    IsHtml = false
                }
            );
            await _ctx.SaveChangesAsync();

            var resolved = await _repo.ResolveInviteTemplateAsync(templateVersionId: 999);
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved!.Id, Is.EqualTo(3));
            Assert.That(resolved.IsDefault, Is.True);
            Assert.That(resolved.ScopeType, Is.EqualTo("Global"));
        }
    }
}
