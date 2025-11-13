using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.CountryDocumentDefinitionMappings;
using IDV_Backend.Repositories.CountryDocumentDefinitionMappings;
using IDV_Backend.Services.CountryDocumentDefinitionMappings;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Services.CountryDocumentDefinitionMappings
{
    public class CountryDocumentDefinitionMappingServiceTests
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

        private static async Task<int> SeedCountryAsync(ApplicationDbContext db, int id, string iso2)
        {
            db.Countries.Add(new IDV_Backend.Models.Countries.Country
            {
                Id = id,
                Name = iso2 + " Country",
                IsoCodeAlpha2 = iso2
            });
            await db.SaveChangesAsync();
            return id;
        }

        private static async Task<DocumentDefinition> SeedDocDefAsync(
            ApplicationDbContext db,
            string iso2,
            string code,
            string name,
            int version = 1)
        {
            var d = new DocumentDefinition
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                CountryIso2 = iso2,
                Category = IDV_Backend.Models.DocumentDefinitions.DocumentCategory.Passport,
                Status = IDV_Backend.Models.DocumentDefinitions.DocumentDefinitionStatus.Published,
                Version = version,
                IsLatest = true,
                MaxFileSizeMb = 5
            };
            db.DocumentDefinitions.Add(d);
            await db.SaveChangesAsync();
            return d;
        }

        [Test]
        public async Task GetMappingsWithDocDetailsAsync_Projects_Fields_Correctly()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var repo = new CountryDocumentDefinitionMappingRepository(db);
            var sut = new CountryDocumentDefinitionMappingService(repo);

            var countryId = await SeedCountryAsync(db, 1, "US");
            var doc = await SeedDocDefAsync(db, "US", "PASS", "Passport");

            db.CountryDocumentDefinitionMappings.Add(new CountryDocumentDefinitionMapping
            {
                CountryId = countryId,
                DocumentDefinitionId = doc.Id,
                IsEnabled = true,
                IsRequiredByDefault = true
            });
            await db.SaveChangesAsync();

            var list = await sut.GetMappingsWithDocDetailsAsync(countryId, onlyEnabled: true);

            list.Should().HaveCount(1);
            var rm = list.Single();
            rm.CountryId.Should().Be(countryId);
            rm.DocumentDefinitionId.Should().Be(doc.Id);
            rm.IsEnabled.Should().BeTrue();
            rm.IsRequiredByDefault.Should().BeTrue();
            rm.Title.Should().Be("Passport");
            rm.Category.Should().Be("Passport");
            rm.Status.Should().Be("Published");
            rm.IsLatest.Should().BeTrue();
            rm.Version.Should().Be(1);
            rm.DocumentCountryIso2.Should().Be("US");
            rm.CountryIso2.Should().Be("US");
        }
    }
}
