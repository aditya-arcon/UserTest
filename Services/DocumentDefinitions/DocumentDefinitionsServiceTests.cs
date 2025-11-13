using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using IDV_Backend.Contracts.DocumentDefinitions;
using IDV_Backend.Data;
using IDV_Backend.Models.DocumentDefinitions;
using IDV_Backend.Repositories.DocumentDefinitions;
using IDV_Backend.Services.Common;
using IDV_Backend.Services.DocumentDefinitions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Services.DocumentDefinitions
{
    public class DocumentDefinitionsServiceTests
    {
        private sealed class TestUser : ICurrentUser
        {
            public long UserId { get; set; } = 7;
            public string? UserName { get; set; } = "unit@tester";
            public string? Email { get; set; } = "unit@tester";
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

        private static DocumentDefinitionsService BuildService(ApplicationDbContext db)
        {
            var repo = new DocumentDefinitionRepository(db);
            var user = new TestUser();
            return new DocumentDefinitionsService(repo, user);
        }

        [Test]
        public async Task CreateAsync_Creates_Draft_And_Marks_Latest()
        {
            await using var db = NewDb(Guid.NewGuid().ToString());
            var sut = BuildService(db);

            var req = new CreateDocumentDefinitionRequest
            {
                Code = "pass",
                Name = "Passport",
                CountryIso2 = "us",
                Category = DocumentCategory.Passport,
                MaxFileSizeMb = 10,
                RequiredFieldsJson = "{\"fields\":[\"number\",\"dob\"]}",
                ValidationRulesJson = "{\"rules\":[{\"field\":\"number\"}]}"
            };

            var created = await sut.CreateAsync(req);

            created.Id.Should().NotBeEmpty();
            created.Code.Should().Be("PASS");
            created.CountryIso2.Should().Be("US");
            created.Status.Should().Be(DocumentDefinitionStatus.Draft);
            created.IsLatest.Should().BeTrue();
            created.Version.Should().Be(1);
        }

        [Test]
        public async Task PublishAsync_Promotes_Draft_And_Demotes_Previous_Published()
        {
            await using var db = NewDb(Guid.NewGuid().ToString());
            var sut = BuildService(db);

            // First draft -> publish
            var d1 = await sut.CreateAsync(new CreateDocumentDefinitionRequest
            {
                Code = "dl",
                Name = "Driver License",
                CountryIso2 = "us",
                Category = DocumentCategory.DriverLicense,
                MaxFileSizeMb = 5,
                RequiredFieldsJson = "{\"fields\":[\"number\"]}",
                ValidationRulesJson = "{\"rules\":[]}"
            });

            var p1 = await sut.PublishAsync(d1.Id, new PublishDocumentDefinitionRequest { ExpectedVersion = d1.Version });

            p1.Status.Should().Be(DocumentDefinitionStatus.Published);
            p1.IsLatest.Should().BeTrue();

            // Create second draft in same lineage and publish
            var d2 = await sut.CreateAsync(new CreateDocumentDefinitionRequest
            {
                Code = "dl",
                Name = "Driver License v2",
                CountryIso2 = "us",
                Category = DocumentCategory.DriverLicense,
                MaxFileSizeMb = 6,
                RequiredFieldsJson = "{\"fields\":[\"number\",\"expiry\"]}",
                ValidationRulesJson = "{\"rules\":[]}"
            });

            var p2 = await sut.PublishAsync(d2.Id, new PublishDocumentDefinitionRequest { ExpectedVersion = d2.Version });
            p2.Version.Should().Be(2);
            p2.IsLatest.Should().BeTrue();

            // Ensure previous published got demoted from latest
            var all = await db.DocumentDefinitions
                .Where(x => x.Code == "DL" && x.CountryIso2 == "US" && x.Status == DocumentDefinitionStatus.Published)
                .OrderBy(x => x.Version)
                .ToListAsync();

            all.Count.Should().Be(2);
            all[0].IsLatest.Should().BeFalse(); // v1
            all[1].IsLatest.Should().BeTrue();  // v2
        }

        [Test]
        public async Task UpdateAsync_Only_Allowed_For_Draft_And_With_Version_Match()
        {
            await using var db = NewDb(Guid.NewGuid().ToString());
            var sut = BuildService(db);

            var d1 = await sut.CreateAsync(new CreateDocumentDefinitionRequest
            {
                Code = "idc",
                Name = "ID Card",
                CountryIso2 = "in",
                Category = DocumentCategory.IdCard,
                MaxFileSizeMb = 4,
                RequiredFieldsJson = "{\"fields\":[\"no\"]}",
                ValidationRulesJson = "{\"rules\":[]}"
            });

            // Update draft OK
            var updated = await sut.UpdateAsync(d1.Id, new UpdateDocumentDefinitionRequest
            {
                ExpectedVersion = d1.Version,
                Name = "ID Card (updated)"
            });
            updated.Name.Should().Contain("updated");

            // Publish
            var pub = await sut.PublishAsync(d1.Id, new PublishDocumentDefinitionRequest { ExpectedVersion = updated.Version });

            // Try update published -> should fail
            Func<Task> act = () => sut.UpdateAsync(pub.Id, new UpdateDocumentDefinitionRequest { ExpectedVersion = pub.Version, Name = "x" });
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Only DRAFT*");
        }

        [Test]
        public async Task CloneToDraftAsync_Creates_New_Line_With_NextVersion_And_Optional_CopyRules()
        {
            await using var db = NewDb(Guid.NewGuid().ToString());
            var sut = BuildService(db);

            var d1 = await sut.CreateAsync(new CreateDocumentDefinitionRequest
            {
                Code = "pp",
                Name = "Passport",
                CountryIso2 = "de",
                Category = DocumentCategory.Passport,
                MaxFileSizeMb = 8,
                RequiredFieldsJson = "{\"fields\":[\"no\"]}",
                ValidationRulesJson = "{\"rules\":[]}",
                SupportedMimeTypesJson = "[\"image/jpeg\"]"
            });

            // Clone with same lineage, copy rules
            var cloned = await sut.CloneToDraftAsync(d1.Id, new CloneDocumentDefinitionRequest
            {
                CopyRules = true
            });

            cloned.Code.Should().Be("PP");
            cloned.CountryIso2.Should().Be("DE");
            cloned.Version.Should().Be(2);
            cloned.IsLatest.Should().BeTrue();
        }

        [Test]
        public async Task DeprecateAsync_Only_Allowed_When_Published()
        {
            await using var db = NewDb(Guid.NewGuid().ToString());
            var sut = BuildService(db);

            var draft = await sut.CreateAsync(new CreateDocumentDefinitionRequest
            {
                Code = "res",
                Name = "Residence Permit",
                CountryIso2 = "nl",
                Category = DocumentCategory.ResidencePermit,
                MaxFileSizeMb = 5,
                RequiredFieldsJson = "{\"fields\":[\"id\"]}",
                ValidationRulesJson = "{\"rules\":[]}"
            });

            Func<Task> act1 = () => sut.DeprecateAsync(draft.Id, "no longer used");
            await act1.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Only PUBLISHED*");

            var pub = await sut.PublishAsync(draft.Id, new PublishDocumentDefinitionRequest { ExpectedVersion = draft.Version });

            var dep = await sut.DeprecateAsync(pub.Id, "retired");
            dep.Status.Should().Be(DocumentDefinitionStatus.Deprecated);
            dep.IsLatest.Should().BeFalse();
        }
    }
}
