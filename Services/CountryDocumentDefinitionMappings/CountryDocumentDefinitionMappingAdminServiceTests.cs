using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.CountryDocumentDefinitionMappings;
using IDV_Backend.Repositories.CountryDocumentDefinitionMappings;
using IDV_Backend.Services.Countries; // only if you had any abstractions; otherwise safe to ignore
using IDV_Backend.Services.CountryDocumentDefinitionMappings;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Services.CountryDocumentDefinitionMappings
{
    public class CountryDocumentDefinitionMappingAdminServiceTests
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
            ApplicationDbContext db, string iso2, string code = "DOC", string name = "Doc")
        {
            var d = new DocumentDefinition
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                CountryIso2 = iso2,
                Category = IDV_Backend.Models.DocumentDefinitions.DocumentCategory.Passport,
                Status = IDV_Backend.Models.DocumentDefinitions.DocumentDefinitionStatus.Published,
                Version = 1,
                IsLatest = true,
                MaxFileSizeMb = 5
            };
            db.DocumentDefinitions.Add(d);
            await db.SaveChangesAsync();
            return d;
        }

        [Test]
        public async Task AddAsync_Succeeds_And_IsIdempotent()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var repo = new CountryDocumentDefinitionMappingRepository(db);
            var sut = new CountryDocumentDefinitionMappingAdminService(db, repo);

            var countryId = await SeedCountryAsync(db, 10, "US");
            var doc = await SeedDocDefAsync(db, "US", "PASS", "Passport");

            await sut.AddAsync(countryId, doc.Id, isEnabled: true, isRequiredByDefault: true);

            var rows = await db.CountryDocumentDefinitionMappings.Where(m => m.CountryId == countryId).ToListAsync();
            rows.Should().HaveCount(1);

            // Call again — should not duplicate
            await sut.AddAsync(countryId, doc.Id, isEnabled: true, isRequiredByDefault: true);
            var rows2 = await db.CountryDocumentDefinitionMappings.Where(m => m.CountryId == countryId).ToListAsync();
            rows2.Should().HaveCount(1);
        }

        [Test]
        public async Task AddAsync_Throws_When_Different_Country()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var repo = new CountryDocumentDefinitionMappingRepository(db);
            var sut = new CountryDocumentDefinitionMappingAdminService(db, repo);

            var countryId = await SeedCountryAsync(db, 44, "GB");
            var doc = await SeedDocDefAsync(db, "US", "PASS", "Passport"); // mismatched ISO2

            Func<Task> act = () => sut.AddAsync(countryId, doc.Id, true, true);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*different country*");
        }

        [Test]
        public async Task ReplaceAllAsync_Removes_All_When_Empty_List()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var repo = new CountryDocumentDefinitionMappingRepository(db);
            var sut = new CountryDocumentDefinitionMappingAdminService(db, repo);

            var countryId = await SeedCountryAsync(db, 99, "DE");
            var d1 = await SeedDocDefAsync(db, "DE", "ID", "ID Card");

            // pre-seed one
            db.CountryDocumentDefinitionMappings.Add(new CountryDocumentDefinitionMapping
            {
                CountryId = countryId,
                DocumentDefinitionId = d1.Id,
                IsEnabled = true,
                IsRequiredByDefault = false
            });
            await db.SaveChangesAsync();

            await sut.ReplaceAllAsync(countryId, Array.Empty<Guid>(), defaultEnabled: true, defaultRequired: false);

            var rows = await db.CountryDocumentDefinitionMappings.Where(m => m.CountryId == countryId).ToListAsync();
            rows.Should().BeEmpty();
        }

        [Test]
        public async Task ReplaceAllAsync_Sets_New_Set_With_Defaults()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var repo = new CountryDocumentDefinitionMappingRepository(db);
            var sut = new CountryDocumentDefinitionMappingAdminService(db, repo);

            var countryId = await SeedCountryAsync(db, 5, "FR");
            var d1 = await SeedDocDefAsync(db, "FR", "PASS", "Passport");
            var d2 = await SeedDocDefAsync(db, "FR", "DL", "Driver License");

            await sut.ReplaceAllAsync(countryId, new[] { d1.Id, d2.Id }, defaultEnabled: true, defaultRequired: false);

            var rows = await db.CountryDocumentDefinitionMappings.Where(m => m.CountryId == countryId).ToListAsync();
            rows.Should().HaveCount(2);
            rows.All(r => r.IsEnabled == true).Should().BeTrue();
            rows.All(r => r.IsRequiredByDefault == false).Should().BeTrue();
        }
    }
}
