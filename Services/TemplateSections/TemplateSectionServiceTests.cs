using FluentValidation;
using IDV_Backend.Contracts.TemplateSection;
using IDV_Backend.Contracts.TemplateSection.Validators;
using IDV_Backend.Contracts.TemplateLogs;
using IDV_Backend.Controllers.TemplateSection;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.TemplateVersion;
using IDV_Backend.Repositories.SectionFieldMappings;
using IDV_Backend.Repositories.TemplateSections;
using IDV_Backend.Repositories.TemplateVersions;
using IDV_Backend.Repositories.Templates;
using IDV_Backend.Services;
using IDV_Backend.Services.Audit;
using IDV_Backend.Services.Common;
using IDV_Backend.Services.TemplateLogs;
using IDV_Backend.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
using System.Security.Claims;

namespace UserTest.Services.TemplateSections;

[TestFixture]
public class TemplateSectionServiceTests
{
    private ApplicationDbContext _db = default!;
    private ITemplateSectionRepository _repo = default!;
    private ISectionFieldMappingRepository _mappingRepo = default!;
    private ITemplateVersionRepository _versionRepo = default!;
    private ITemplateRepository _templateRepo = default!;
    private TemplateSectionService _svc = default!;
    private Mock<ICurrentUser> _me = default!;
    private Mock<ITemplateAuditLogger> _audit = default!;

    [SetUp]
    public void SetUp()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(opts);

        // Seed a template and its version
        _db.Templates.Add(new IDV_Backend.Models.Template
        {
            Id = 1,
            Name = "Employee Onboarding"
        });

        _db.TemplateVersions.Add(new TemplateVersion
        {
            VersionId = 99,
            TemplateId = 1,
            VersionNumber = 1,
            Status = TemplateVersionStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // Seed a user (creator)
        _db.Users.Add(new IDV_Backend.Models.User.User { Id = 777, Email = "admin@idv.local", FirstName = "A", LastName = "D" });
        _db.SaveChanges();

        _repo = new TemplateSectionRepository(_db);
        _mappingRepo = new SectionFieldMappingRepository(_db);
        _versionRepo = new TemplateVersionRepository(_db);
        _templateRepo = new TemplateRepository(_db);

        _me = new Mock<ICurrentUser>();
        _me.SetupGet(x => x.UserId).Returns(777);
        _me.SetupGet(x => x.UserName).Returns("Admin D");

        _audit = new Mock<ITemplateAuditLogger>();
        _audit.Setup(x => x.LogAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        _svc = new TemplateSectionService(
            _repo,
            _mappingRepo,
            _versionRepo,
            _me.Object,
            _audit.Object,
            new CreateTemplateSectionDtoValidator(),
            new UpdateTemplateSectionDtoValidator(),
            null);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task Create_Then_Get_Works()
    {
        var created = await _svc.CreateSectionAsync(99, new CreateTemplateSectionDto
        {
            Name = "Personal Info",
            SectionType = SectionTypes.PersonalInformation,
            IsActive = true
        }, currentUserId: 777, CancellationToken.None);

        Assert.That(created.Id, Is.GreaterThan(0));
        Assert.That(created.TemplateVersionId, Is.EqualTo(99));
        Assert.That(created.Name, Is.EqualTo("Personal Info"));

        var byId = await _svc.GetSectionByIdAsync(created.Id, CancellationToken.None);
        Assert.That(byId, Is.Not.Null);
        Assert.That(byId!.Id, Is.EqualTo(created.Id));

        // Verify audit logger got called with correct version id
        _audit.Verify(x => x.LogAsync(
            It.Is<long>(vid => vid == 99),
            It.Is<long>(uid => uid == 777),
            It.IsAny<string?>(),
            It.Is<string>(a => a == IDV_Backend.Constants.AuditActions.SectionCreated),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetByVersion_ActiveAndAll_Works()
    {
        await _svc.CreateSectionAsync(99, new CreateTemplateSectionDto { Name = "PI", SectionType = SectionTypes.PersonalInformation, IsActive = true }, 777, CancellationToken.None);
        await _svc.CreateSectionAsync(99, new CreateTemplateSectionDto { Name = "Docs", SectionType = SectionTypes.Documents, IsActive = false }, 777, CancellationToken.None);

        var all = await _svc.GetSectionsByVersionIdAsync(99, CancellationToken.None);
        var active = await _svc.GetActiveSectionsByVersionIdAsync(99, CancellationToken.None);

        Assert.That(all.Count, Is.EqualTo(2));
        Assert.That(active.Count, Is.EqualTo(1));
        Assert.That(active[0].SectionType, Is.EqualTo(SectionTypes.PersonalInformation));
    }

    [Test]
    public async Task Update_Order_And_Activation_Rules_Work()
    {
        var a = await _svc.CreateSectionAsync(99, new CreateTemplateSectionDto { Name = "A", SectionType = SectionTypes.PersonalInformation, IsActive = true }, 777, CancellationToken.None);
        var b = await _svc.CreateSectionAsync(99, new CreateTemplateSectionDto { Name = "B", SectionType = SectionTypes.Documents, IsActive = true }, 777, CancellationToken.None);

        // Move B to order 1
        var updatedB = await _svc.UpdateSectionAsync(b.Id, new UpdateTemplateSectionDto { OrderIndex = 1 }, 777, CancellationToken.None);
        Assert.That(updatedB, Is.Not.Null);
        Assert.That(updatedB!.OrderIndex, Is.EqualTo(1));

        // Deactivate A
        var updatedA = await _svc.UpdateSectionAsync(a.Id, new UpdateTemplateSectionDto { IsActive = false }, 777, CancellationToken.None);
        Assert.That(updatedA, Is.Not.Null);
        Assert.That(updatedA!.IsActive, Is.False);

        // Verify audit logger was called for update
        _audit.Verify(x => x.LogAsync(
            It.Is<long>(vid => vid == 99),
            It.Is<long>(uid => uid == 777),
            It.IsAny<string?>(),
            It.Is<string>(a => a == IDV_Backend.Constants.AuditActions.SectionUpdated),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Test]
    public async Task Delete_Reorders_Remaining()
    {
        var a = await _svc.CreateSectionAsync(99, new CreateTemplateSectionDto { Name = "A", SectionType = SectionTypes.PersonalInformation, IsActive = true }, 777, CancellationToken.None);
        var b = await _svc.CreateSectionAsync(99, new CreateTemplateSectionDto { Name = "B", SectionType = SectionTypes.Documents, IsActive = true }, 777, CancellationToken.None);

        var ok = await _svc.DeleteSectionAsync(a.Id, 777, CancellationToken.None);
        Assert.That(ok, Is.True);

        var all = await _svc.GetActiveSectionsByVersionIdAsync(99, CancellationToken.None);
        Assert.That(all.Count, Is.EqualTo(1));
        Assert.That(all[0].OrderIndex, Is.EqualTo(1)); // compacted
        Assert.That(all[0].Id, Is.EqualTo(b.Id));

        // Verify audit logger was called for delete
        _audit.Verify(x => x.LogAsync(
            It.Is<long>(vid => vid == 99),
            It.Is<long>(uid => uid == 777),
            It.IsAny<string?>(),
            It.Is<string>(a => a == IDV_Backend.Constants.AuditActions.SectionDeleted),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // === New: Controller-level test to ensure logs persist real TemplateId ===
    [Test]
    public async Task Controller_Logs_Resolve_TemplateId_From_Version()
    {
        // Arrange controller with real services
        var templateLogs = new TemplateLogService(_db, Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplateLogService>.Instance);
        var controller = new TemplateSectionController(_svc, templateLogs, _versionRepo, _templateRepo);

        // Fake admin identity
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "777"),
            new Claim(ClaimTypes.Name, "Admin D"),
            new Claim(ClaimTypes.Email, "admin@idv.local"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act: create a section via controller
        var dto = new CreateTemplateSectionDto
        {
            Name = "Controller PI",
            SectionType = SectionTypes.PersonalInformation,
            IsActive = true
        };
        var result = await controller.CreateSection(99, dto, CancellationToken.None);
        Assert.That(result, Is.TypeOf<CreatedAtActionResult>());

        // Assert: a template log was written with the correct TemplateId (1) and VersionId (99)
        var log = await _db.TemplateLogs.OrderByDescending(l => l.Id).FirstOrDefaultAsync();
        Assert.That(log, Is.Not.Null);
        Assert.That(log!.TemplateId, Is.EqualTo(1));
        Assert.That(log.VersionId, Is.EqualTo(99));
        Assert.That(log.TemplateName, Is.EqualTo("Employee Onboarding"));
        Assert.That(log.Action, Is.EqualTo(IDV_Backend.Models.TemplateLogs.TemplateAction.SectionAdd));
    }
}
