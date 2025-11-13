using IDV_Backend.Contracts.QRCode;
using IDV_Backend.Data;
using IDV_Backend.Models.QRCode;
// ✅ bring the correct namespace for the TemplatesLink entity into scope
// If your project’s model is under IDV_Backend.Models.TemplatesLinks, use that:
using IDV_Backend.Models.TemplatesLinkGenerations;
using IDV_Backend.Repositories.QRCodes;
using IDV_Backend.Services.QRCode;
using IDV_Backend.Services.Security;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Models.UserTemplateSubmissions;

namespace UserTest.Services.QRCodes
{
    [TestFixture]
    public class QRCodeServiceTests
    {
        private ApplicationDbContext _db = default!;
        private IQRCodeRepository _repo = default!;
        private Mock<ILinkTokenService> _token = default!;
        private IQRCodeService _svc = default!;

        private const long UserId = 901;
        private const long TemplateVersionId = 44;
        private const long SubmissionId = 555;
        private const long SectionId = 777;
        private const long SrmId = 321;

        [SetUp]
        public void SetUp()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            _db = new ApplicationDbContext(opts);

            // --- Seed minimal graph for TryResolveShortCodeForSectionAsync ---
            // User
            _db.Users.Add(new IDV_Backend.Models.User.User
            {
                Id = UserId,
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada@idv.local",
                Phone = "000"
            });

            // Submission
            _db.UserTemplateSubmissions.Add(new IDV_Backend.Models.UserTemplateSubmissions.UserTemplateSubmission
            {
                Id = SubmissionId,
                UserId = UserId,
                TemplateVersionId = TemplateVersionId,
                // ✅ fully qualify the enum so it compiles
                Status = SubmissionStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                IsDeleted = false
            });

            // SectionResponseMapping (ties a submission to a section)
            _db.SectionResponseMappings.Add(new IDV_Backend.Models.SectionResponseMapping.SectionResponseMapping
            {
                Id = SrmId,
                UserTemplateSubmissionRef = SubmissionId,
                TemplateSectionRef = SectionId
            });

            // Templates Link (active)
            // ✅ use the correct namespace for the model
            _db.TemplatesLinks.Add(new TemplatesLinkGeneration
            {
                UserId = UserId,
                TemplateVersionId = TemplateVersionId,
                ShortCodeHash = "hash-doesnt-matter-for-test",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

            _db.SaveChanges();

            _repo = new QRCodeRepository(_db);

            _token = new Mock<ILinkTokenService>();
            _token.Setup(t => t.GenerateToken(It.IsAny<LinkTokenPayload>()))
                  .Returns("SHORTCODE123");

            // Prefer ctor with repository (clean dependency separation)
            _svc = new QRCodeService(_db, _repo, _token.Object);
        }

        [TearDown]
        public void TearDown() => _db.Dispose();

        [Test]
        public async Task Create_WithProvidedCode_UsesThatCode()
        {
            var req = new CreateQRCodeRequest
            {
                SectionResponseMappingId = SrmId,
                ExpiryTime = DateTime.UtcNow.AddMinutes(10),
                Code = "MY_CUSTOM_CODE"
            };

            var created = await _svc.CreateAsync(req, CancellationToken.None);

            // Should not have called token service when a code was provided
            _token.Verify(t => t.GenerateToken(It.IsAny<LinkTokenPayload>()), Times.Never);

            Assert.That(created.QrId, Is.GreaterThan(0));
            StringAssert.StartsWith("https://idv.example.com/form/", created.GeneratedCode);
            StringAssert.EndsWith("MY_CUSTOM_CODE", created.GeneratedCode);
            Assert.That(created.ExpiryTime, Is.GreaterThan(DateTime.UtcNow));
            Assert.That(created.ImageBase64, Is.Not.Empty);

            // DB row exists
            var row = await _db.QRCodes.AsNoTracking().FirstOrDefaultAsync(q => q.QrId == created.QrId);
            Assert.That(row, Is.Not.Null);
            Assert.That(row!.GeneratedCode, Is.EqualTo("MY_CUSTOM_CODE"));
        }

        [Test]
        public async Task Create_WithoutCode_ResolvesShortCode_FromTemplatesLink()
        {
            var req = new CreateQRCodeRequest
            {
                SectionResponseMappingId = SrmId,
                ExpiryTime = DateTime.UtcNow.AddMinutes(15)
            };

            var created = await _svc.CreateAsync(req, CancellationToken.None);

            _token.Verify(t => t.GenerateToken(It.IsAny<LinkTokenPayload>()), Times.AtLeastOnce);

            Assert.That(created.QrId, Is.GreaterThan(0));
            StringAssert.EndsWith("SHORTCODE123", created.GeneratedCode);
            Assert.That(created.ImageBase64, Is.Not.Empty);
        }

        [Test]
        public async Task GetById_Works()
        {
            // create one
            var c = await _svc.CreateAsync(new CreateQRCodeRequest
            {
                SectionResponseMappingId = SrmId,
                ExpiryTime = DateTime.UtcNow.AddMinutes(5),
                Code = "HELLO_1"
            }, CancellationToken.None);

            var byId = await _svc.GetByIdAsync(c.QrId, CancellationToken.None);
            Assert.That(byId, Is.Not.Null);
            Assert.That(byId!.QrId, Is.EqualTo(c.QrId));
            Assert.That(byId.ImageBase64, Is.Not.Empty);
        }

        [Test]
        public async Task GetAll_NoImages_And_WithImages_Work()
        {
            // seed two
            await _svc.CreateAsync(new CreateQRCodeRequest
            {
                SectionResponseMappingId = SrmId,
                ExpiryTime = DateTime.UtcNow.AddMinutes(5),
                Code = "A1"
            }, CancellationToken.None);
            await _svc.CreateAsync(new CreateQRCodeRequest
            {
                SectionResponseMappingId = SrmId,
                ExpiryTime = DateTime.UtcNow.AddMinutes(6),
                Code = "A2"
            }, CancellationToken.None);

            var listNoImages = (await _svc.GetAllAsync(includeImages: false, CancellationToken.None)).ToList();
            Assert.That(listNoImages.Count, Is.EqualTo(2));
            Assert.That(listNoImages.All(x => string.IsNullOrEmpty(x.ImageBase64)));

            var listWithImages = (await _svc.GetAllAsync(includeImages: true, CancellationToken.None)).ToList();
            Assert.That(listWithImages.Count, Is.EqualTo(2));
            Assert.That(listWithImages.All(x => !string.IsNullOrEmpty(x.ImageBase64)));
        }

        [Test]
        public async Task GetImage_ReturnsBytesAndMime()
        {
            var c = await _svc.CreateAsync(new CreateQRCodeRequest
            {
                SectionResponseMappingId = SrmId,
                ExpiryTime = DateTime.UtcNow.AddMinutes(5),
                Code = "IMG_CODE"
            }, CancellationToken.None);

            var img = await _svc.GetImageAsync(c.QrId, CancellationToken.None);
            Assert.That(img, Is.Not.Null);
            Assert.That(img!.Value.bytes.Length, Is.GreaterThan(0));
            Assert.That(img.Value.mime, Is.EqualTo("image/png"));
        }

        [Test]
        public async Task Delete_RemovesRow()
        {
            var c = await _svc.CreateAsync(new CreateQRCodeRequest
            {
                SectionResponseMappingId = SrmId,
                ExpiryTime = DateTime.UtcNow.AddMinutes(5),
                Code = "DEL_CODE"
            }, CancellationToken.None);

            var ok = await _svc.DeleteAsync(c.QrId, CancellationToken.None);
            Assert.That(ok, Is.True);

            var byId = await _svc.GetByIdAsync(c.QrId, CancellationToken.None);
            Assert.That(byId, Is.Null);
        }

        [Test]
        public void Create_Rejects_PastExpiry()
        {
            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _svc.CreateAsync(new CreateQRCodeRequest
                {
                    SectionResponseMappingId = SrmId,
                    ExpiryTime = DateTime.UtcNow.AddMinutes(-1),
                    Code = "X"
                }, CancellationToken.None);
            });

            StringAssert.Contains("ExpiryTime must be in the future", ex!.Message);
        }

        [Test]
        public void Create_Rejects_MissingSectionResponseMapping()
        {
            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _svc.CreateAsync(new CreateQRCodeRequest
                {
                    SectionResponseMappingId = 999999, // not seeded
                    ExpiryTime = DateTime.UtcNow.AddMinutes(10),
                    Code = "Y"
                }, CancellationToken.None);
            });

            StringAssert.Contains("SectionResponseMapping 999999 not found", ex!.Message);
        }
    }
}
