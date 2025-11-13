using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.CountryDocumentDefinitionMappings;
using IDV_Backend.Repositories.CountryDocumentDefinitionMappings;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.CountryDocumentDefinitionMappings
{
    public class CountryDocumentDefinitionMappingRepositoryTests
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

        private static async Task<int> SeedCountryAsync(ApplicationDbContext db, int id = 1, string iso2 = "US")
        {
            db.Countries.Add(new IDV_Backend.Models.Countries.Country
            {
                Id = id,
                Name = "United States",
                IsoCodeAlpha2 = iso2
            });
            await db.SaveChangesAsync();
            return id;
        }

        private static async Task<DocumentDefinition> SeedDocDefAsync(
            ApplicationDbContext db,
            Guid? id = null,
            string code = "PASSPORT",
            string name = "Passport",
            string iso2 = "US",
            int version = 1)
        {
            var entity = new DocumentDefinition
            {
                Id = id ?? Guid.NewGuid(),
                Code = code,
                Name = name,
                CountryIso2 = iso2,
                Category = IDV_Backend.Models.DocumentDefinitions.DocumentCategory.Passport,
                Status = IDV_Backend.Models.DocumentDefinitions.DocumentDefinitionStatus.Published,
                Version = version,
                IsLatest = true,
                MaxFileSizeMb = 5
            };
            db.DocumentDefinitions.Add(entity);
            await db.SaveChangesAsync();
            return entity;
        }

        [Test]
        public async Task GetByCountryIdAsync_Returns_Only_Enabled_When_Flag_True()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var repo = new CountryDocumentDefinitionMappingRepository(db);

            var countryId = await SeedCountryAsync(db, 99, "US");
            var d1 = await SeedDocDefAsync(db, iso2: "US", code: "PASS");
            var d2 = await SeedDocDefAsync(db, iso2: "US", code: "DL");

            db.CountryDocumentDefinitionMappings.AddRange(
                new CountryDocumentDefinitionMapping
                {
                    CountryId = countryId,
                    DocumentDefinitionId = d1.Id,
                    IsEnabled = true,
                    IsRequiredByDefault = true
                },
                new CountryDocumentDefinitionMapping
                {
                    CountryId = countryId,
                    DocumentDefinitionId = d2.Id,
                    IsEnabled = false,
                    IsRequiredByDefault = false
                }
            );
            await db.SaveChangesAsync();

            var onlyEnabled = await repo.GetByCountryIdAsync(countryId, onlyEnabled: true);
            onlyEnabled.Select(x => x.DocumentDefinitionId).Should().Contain(d1.Id)
                       .And.NotContain(d2.Id);

            var all = await repo.GetByCountryIdAsync(countryId, onlyEnabled: false);
            all.Select(x => x.DocumentDefinitionId).Should().Contain(new[] { d1.Id, d2.Id });
        }

        [Test]
        public async Task ReplaceAllAsync_Removes_And_Adds_As_Expected()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var repo = new CountryDocumentDefinitionMappingRepository(db);

            var countryId = await SeedCountryAsync(db, 77, "GB");
            var d1 = await SeedDocDefAsync(db, iso2: "GB", code: "PASS");
            var d2 = await SeedDocDefAsync(db, iso2: "GB", code: "DL");
            var d3 = await SeedDocDefAsync(db, iso2: "GB", code: "RES");

            // Start with d1 enabled
            db.CountryDocumentDefinitionMappings.Add(new CountryDocumentDefinitionMapping
            {
                CountryId = countryId,
                DocumentDefinitionId = d1.Id,
                IsEnabled = true,
                IsRequiredByDefault = false
            });
            await db.SaveChangesAsync();

            // Replace with d2 + d3
            await repo.ReplaceAllAsync(countryId, new[]
            {
                new CountryDocumentDefinitionMapping { DocumentDefinitionId = d2.Id, IsEnabled = true,  IsRequiredByDefault = true  },
                new CountryDocumentDefinitionMapping { DocumentDefinitionId = d3.Id, IsEnabled = false, IsRequiredByDefault = false }
            }, default);

            var rows = await db.CountryDocumentDefinitionMappings
                .Where(m => m.CountryId == countryId)
                .OrderBy(m => m.DocumentDefinitionId)
                .ToListAsync();

            rows.Should().HaveCount(2);
            rows.Select(r => r.DocumentDefinitionId).Should().BeEquivalentTo(new[] { d2.Id, d3.Id });
        }
    }
}
