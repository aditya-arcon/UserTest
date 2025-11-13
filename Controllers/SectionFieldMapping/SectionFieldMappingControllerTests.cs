using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Contracts.SectionFieldMapping;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.TemplateLogs;
using IDV_Backend.Repositories.TemplateSections;
using IDV_Backend.Repositories.TemplateVersions;
using IDV_Backend.Services.SectionFieldMapping;
using IDV_Backend.Services.TemplateLogs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace UserTest.Controllers.SectionFieldMapping
{
    [TestFixture]
    public class SectionFieldMappingControllerTests
    {
        private ApplicationDbContext _db = null!;
        private Mock<ISectionFieldMappingService> _svc = null!;
        private Mock<ITemplateActionLogger> _logger = null!;
        private Mock<ITemplateSectionRepository> _sections = null!;
        private Mock<ITemplateVersionRepository> _versions = null!;

        private IDV_Backend.Controllers.SectionFieldMapping.SectionFieldMappingController _controller = null!;

        [SetUp]
        public void SetUp()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: "SectionFieldMappingControllerTests")
                .Options;
            _db = new ApplicationDbContext(opts);

            // Seed one template
            _db.Set<Template>().Add(new Template { Id = 1001, Name = "KYC Onboarding", NameNormalized = "KYC ONBOARDING" });
            _db.SaveChanges();

            _svc = new Mock<ISectionFieldMappingService>(MockBehavior.Strict);
            _logger = new Mock<ITemplateActionLogger>(MockBehavior.Strict);
            _sections = new Mock<ITemplateSectionRepository>(MockBehavior.Strict);
            _versions = new Mock<ITemplateVersionRepository>(MockBehavior.Strict);

            _controller = new IDV_Backend.Controllers.SectionFieldMapping.SectionFieldMappingController(
                _svc.Object,
                _logger.Object,
                _sections.Object,
                _versions.Object,
                _db
            );
        }

        [TearDown]
        public void TearDown() => _db.Database.EnsureDeleted();

        [Test]
        public async Task Create_Logs_WithResolvedTemplateId()
        {
            // Arrange
            var sectionId = 11L;
            var versionId = 22L;
            var templateId = 1001L;

            _svc.Setup(s => s.CreateAsync(It.IsAny<CreateSectionFieldMappingDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SectionFieldMappingDto
                {
                    Id = 5,
                    TemplateSectionId = sectionId
                });

            _sections.Setup(r => r.GetByIdAsync(sectionId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new TemplateSection { Id = sectionId, TemplateVersionId = versionId });

            _versions.Setup(r => r.GetByIdAsync(versionId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new IDV_Backend.Models.TemplateVersion.TemplateVersion
                     {
                         VersionId = versionId,
                         TemplateId = templateId
                     });

            _logger.Setup(l => l.LogAsync(
                    templateId,                         // templateId
                    "KYC Onboarding",                   // templateName
                    TemplateAction.FieldAdd,            // action
                    It.IsAny<string>(),                 // changeSummary
                    It.IsAny<long?>(),                  // versionId (optional)
                    It.IsAny<string?>(),                // versionNumber (optional)
                    It.IsAny<object?>(),                // changeDetails (optional)
                    "Field",                            // entityType (optional)
                    5L,                                 // entityId (optional)
                    It.IsAny<CancellationToken>()))     // ct (optional)
                .ReturnsAsync(1L);

            // Act
            var actionResult = await _controller.Create(new CreateSectionFieldMappingDto
            {
                TemplateSectionId = sectionId
            }, CancellationToken.None);

            // Assert
            Assert.That(actionResult, Is.InstanceOf<CreatedAtActionResult>());
            _logger.VerifyAll();
        }

        [Test]
        public async Task Update_Logs_WithResolvedTemplateId()
        {
            var sectionId = 12L;
            var versionId = 23L;
            var templateId = 1001L;

            _svc.Setup(s => s.UpdateAsync(7, It.IsAny<UpdateSectionFieldMappingDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SectionFieldMappingDto
                {
                    Id = 7,
                    TemplateSectionId = sectionId
                });

            _sections.Setup(r => r.GetByIdAsync(sectionId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new TemplateSection { Id = sectionId, TemplateVersionId = versionId });

            _versions.Setup(r => r.GetByIdAsync(versionId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new IDV_Backend.Models.TemplateVersion.TemplateVersion
                     {
                         VersionId = versionId,
                         TemplateId = templateId
                     });

            _logger.Setup(l => l.LogAsync(
                    templateId,
                    "KYC Onboarding",
                    TemplateAction.FieldUpdate,
                    It.IsAny<string>(),
                    It.IsAny<long?>(),
                    It.IsAny<string?>(),
                    It.IsAny<object?>(),
                    "Field",
                    7L,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);

            var result = await _controller.Update(7, new UpdateSectionFieldMappingDto(), CancellationToken.None);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            _logger.VerifyAll();
        }

        [Test]
        public async Task Patch_Logs_WithResolvedTemplateId()
        {
            var sectionId = 13L;
            var versionId = 24L;
            var templateId = 1001L;

            _svc.Setup(s => s.PatchBySectionIdAsync(sectionId, It.IsAny<UpdateSectionFieldMappingDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SectionFieldMappingDto
                {
                    Id = 9,
                    TemplateSectionId = sectionId
                });

            _sections.Setup(r => r.GetByIdAsync(sectionId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new TemplateSection { Id = sectionId, TemplateVersionId = versionId });

            _versions.Setup(r => r.GetByIdAsync(versionId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new IDV_Backend.Models.TemplateVersion.TemplateVersion
                     {
                         VersionId = versionId,
                         TemplateId = templateId
                     });

            _logger.Setup(l => l.LogAsync(
                    templateId,
                    "KYC Onboarding",
                    TemplateAction.FieldUpdate,
                    It.IsAny<string>(),
                    It.IsAny<long?>(),
                    It.IsAny<string?>(),
                    It.IsAny<object?>(),
                    "Field",
                    9L,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);

            var result = await _controller.PatchBySectionId(sectionId, new UpdateSectionFieldMappingDto(), CancellationToken.None);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            _logger.VerifyAll();
        }

        [Test]
        public async Task Delete_Logs_WithResolvedTemplateId()
        {
            var sectionId = 14L;
            var versionId = 25L;
            var templateId = 1001L;

            _svc.Setup(s => s.GetByIdAsync(15, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SectionFieldMappingDto
                {
                    Id = 15,
                    TemplateSectionId = sectionId
                });

            _svc.Setup(s => s.DeleteAsync(15, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            _sections.Setup(r => r.GetByIdAsync(sectionId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new TemplateSection { Id = sectionId, TemplateVersionId = versionId });

            _versions.Setup(r => r.GetByIdAsync(versionId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new IDV_Backend.Models.TemplateVersion.TemplateVersion
                     {
                         VersionId = versionId,
                         TemplateId = templateId
                     });

            _logger.Setup(l => l.LogAsync(
                    templateId,
                    "KYC Onboarding",
                    TemplateAction.FieldRemove,
                    It.IsAny<string>(),
                    It.IsAny<long?>(),
                    It.IsAny<string?>(),
                    It.IsAny<object?>(),
                    "Field",
                    15L,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);

            var result = await _controller.Delete(15, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<NoContentResult>());
            _logger.VerifyAll();
        }
    }
}
