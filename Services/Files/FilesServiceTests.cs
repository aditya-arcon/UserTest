using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IDV_Backend.Contracts.Files;
using IDV_Backend.Contracts.Files.Validators;
using IDV_Backend.Data;
using IDV_Backend.Models.Files;
using IDV_Backend.Repositories.FileTableMappingRepo;
using IDV_Backend.Repositories.Files;
using IDV_Backend.Services.Common;
using IDV_Backend.Services.Files;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

// Explicit, namespace-safe aliases
using DocumentDefinitionEntity = IDV_Backend.Models.DocumentDefinition;
using DocumentDefinitionStatus = IDV_Backend.Models.DocumentDefinitions.DocumentDefinitionStatus;
using UserTemplateSubmission = IDV_Backend.Models.UserTemplateSubmissions.UserTemplateSubmission;

namespace UserTest.Services.Files
{
    public class FilesServiceTests
    {
        private sealed class TestUser : ICurrentUser
        {
            public long UserId { get; set; } = 42;
            public string? UserName { get; set; } = "tester@example.com";
            public string? Email { get; set; } = "tester@example.com";
        }

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

        private static async Task SeedDefinitionAsync(
            ApplicationDbContext db,
            Guid defId,
            string[]? mimes = null,
            int maxMb = 5)
        {
            db.DocumentDefinitions.Add(new DocumentDefinitionEntity
            {
                Id = defId,
                // REQUIRED FIELDS in your model:
                Code = "PASSPORT",       // <- add required Code
                CountryIso2 = "US",      // <- add required CountryIso2

                Name = "Passport",
                MaxFileSizeMb = maxMb,
                SupportedMimeTypesJson = mimes is null ? null : System.Text.Json.JsonSerializer.Serialize(mimes),
                Status = DocumentDefinitionStatus.Published
            });

            await db.SaveChangesAsync();
        }

        private static async Task<long> SeedSubmissionAsync(ApplicationDbContext db, bool isDeleted = false)
        {
            var s = new UserTemplateSubmission
            {
                // Your model has TemplateVersionId as long
                TemplateVersionId = 1L,
                UserId = 111,
                IsDeleted = isDeleted
            };
            db.UserTemplateSubmissions.Add(s);
            await db.SaveChangesAsync();
            return s.Id;
        }

        private static FilesService BuildService(ApplicationDbContext db)
        {
            var createV = new CreateFileRequestValidator();
            var updateV = new UpdateFileRequestValidator();
            var user = new TestUser();

            var fileRepo = new FileRecordRepository(db);
            var mapRepo = new FileTableMappingRepository(db);

            return new FilesService(db, user, createV, updateV, fileRepo, mapRepo);
        }

        [Test]
        public async Task CreateAsync_Succeeds_WithAllowedMimeAndSize()
        {
            // Arrange
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var defId = Guid.NewGuid();
            await SeedDefinitionAsync(db, defId, new[] { "image/jpeg", "application/pdf" }, maxMb: 5);
            var sut = BuildService(db);

            var req = new CreateFileRequest
            {
                FileName = "photo.jpg",
                ContentType = "image/jpeg",
                Length = 1024 * 1024, // 1MB
                DocumentDefinitionId = defId,
                StorageBucket = "uploads",
                StorageObjectKey = "2025/10/file"
            };

            // Act
            var created = await sut.CreateAsync(req);

            // Assert
            created.Id.Should().BeGreaterThan(0);
            created.FileName.Should().Be("photo.jpg");
            created.ContentType.Should().Be("image/jpeg");
            created.DocumentDefinitionId.Should().Be(defId);

            var row = await db.Files.FirstAsync(f => f.Id == created.Id);
            row.Status.Should().Be(FileStatus.Active);
        }

        [Test]
        public async Task CreateAsync_Throws_WhenContentTypeNotAllowed()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var defId = Guid.NewGuid();
            await SeedDefinitionAsync(db, defId, new[] { "image/jpeg" }, maxMb: 5);
            var sut = BuildService(db);

            var req = new CreateFileRequest
            {
                FileName = "scan.png",
                ContentType = "image/png",
                Length = 10_000,
                DocumentDefinitionId = defId,
                StorageBucket = "uploads",
                StorageObjectKey = "2025/10/scan.png"
            };

            Func<Task> act = () => sut.CreateAsync(req);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*not allowed*");
        }

        [Test]
        public async Task CreateWithMappingAsync_Creates_File_And_Mapping()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var defId = Guid.NewGuid();
            await SeedDefinitionAsync(db, defId, new[] { "application/pdf" }, maxMb: 10);
            var submissionId = await SeedSubmissionAsync(db);
            var sut = BuildService(db);

            var req = new CreateFileRequest
            {
                FileName = "doc.pdf",
                ContentType = "application/pdf",
                Length = 512_000,
                DocumentDefinitionId = defId,
                StorageBucket = "uploads",
                StorageObjectKey = "2025/10/doc.pdf"
            };

            var resp = await sut.CreateWithMappingAsync(req, submissionId);

            resp.File.Id.Should().BeGreaterThan(0);
            resp.Mapping.Should().NotBeNull();
            resp.Mapping!.Success.Should().BeTrue();

            var mapping = await db.FileTableMappings
                .FirstOrDefaultAsync(m => m.FileId == resp.File.Id && m.UserTemplateSubmissionId == submissionId);
            mapping.Should().NotBeNull();
            mapping!.IsDeleted.Should().BeFalse();
        }

        [Test]
        public async Task GetFilesBySubmissionIdAsync_Returns_Only_Mapped()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var defId = Guid.NewGuid();
            await SeedDefinitionAsync(db, defId, new[] { "image/jpeg" }, 5);
            var submissionId = await SeedSubmissionAsync(db);
            var sut = BuildService(db);

            // one mapped
            var f1 = await sut.CreateWithMappingAsync(new CreateFileRequest
            {
                FileName = "a.jpg",
                ContentType = "image/jpeg",
                Length = 10_000,
                DocumentDefinitionId = defId,
                StorageBucket = "uploads",
                StorageObjectKey = "mapped/a.jpg"
            }, submissionId);

            // one not mapped
            _ = await sut.CreateAsync(new CreateFileRequest
            {
                FileName = "b.jpg",
                ContentType = "image/jpeg",
                Length = 10_000,
                DocumentDefinitionId = defId,
                StorageBucket = "uploads",
                StorageObjectKey = "unmapped/b.jpg"
            });

            var list = await sut.GetFilesBySubmissionIdAsync(submissionId);
            list.Should().ContainSingle(x => x.Id == f1.File.Id);
        }

        [Test]
        public async Task SoftDeleteAsync_Marks_File_And_Mappings_As_Deleted()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var defId = Guid.NewGuid();
            await SeedDefinitionAsync(db, defId, new[] { "image/jpeg" }, 5);
            var submissionId = await SeedSubmissionAsync(db);
            var sut = BuildService(db);

            var created = await sut.CreateWithMappingAsync(new CreateFileRequest
            {
                FileName = "a.jpg",
                ContentType = "image/jpeg",
                Length = 10_000,
                DocumentDefinitionId = defId,
                StorageBucket = "uploads",
                StorageObjectKey = "mapped/a.jpg"
            }, submissionId);

            var ok = await sut.SoftDeleteAsync(created.File.Id);
            ok.Should().BeTrue();

            var file = await db.Files.IgnoreQueryFilters().FirstAsync(f => f.Id == created.File.Id);
            file.IsDeleted.Should().BeTrue();
            file.Status.Should().Be(FileStatus.Deleted);

            var mapping = await db.FileTableMappings.IgnoreQueryFilters().FirstAsync(m => m.FileId == created.File.Id);
            mapping.IsDeleted.Should().BeTrue();
        }

        [Test]
        public async Task SearchAsync_Filters_And_Paginates()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var defId = Guid.NewGuid();
            await SeedDefinitionAsync(db, defId, new[] { "image/jpeg" }, 5);
            var sut = BuildService(db);

            for (int i = 0; i < 30; i++)
            {
                await sut.CreateAsync(new CreateFileRequest
                {
                    FileName = i % 2 == 0 ? $"cat-{i}.jpg" : $"dog-{i}.jpg",
                    ContentType = "image/jpeg",
                    Length = 1_000,
                    DocumentDefinitionId = defId,
                    StorageBucket = "uploads",
                    StorageObjectKey = $"search/{i}.jpg"
                });
            }

            var page1 = await sut.SearchAsync(defId, FileStatus.Active, "cat", page: 1, pageSize: 5);
            page1.Items.Should().HaveCount(5);
            page1.TotalCount.Should().Be(15);

            var page2 = await sut.SearchAsync(defId, FileStatus.Active, "cat", page: 2, pageSize: 5);
            page2.Items.Should().HaveCount(5);
        }
    }
}
