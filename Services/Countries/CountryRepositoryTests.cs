using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IDV_Backend.Data;
using IDV_Backend.Models.Countries;
using IDV_Backend.Repositories.Countries;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.Countries
{
    public sealed class CountryRepositoryTests
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

        private static async Task<Country> SeedAsync(ApplicationDbContext db, int id, string name, string iso2, string iso3, bool isActive = true, IEnumerable<DocumentType>? types = null)
        {
            var c = new Country
            {
                Id = id,
                Name = name,
                IsoCodeAlpha2 = iso2,
                IsoCodeAlpha3 = iso3,
                IsActive = isActive,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            c.DocumentTypes = types?.ToList() ?? new List<DocumentType>
            {
                new DocumentType { Id = 1, Name = "Passport", Code = "PASS" }
            };
            db.Countries.Add(c);
            await db.SaveChangesAsync();
            return c;
        }

        private static async Task SeedMappingAsync(ApplicationDbContext db, int countryId, Guid docId)
        {
            db.CountryDocumentDefinitionMappings.Add(new IDV_Backend.Models.CountryDocumentDefinitionMappings.CountryDocumentDefinitionMapping
            {
                CountryId = countryId,
                DocumentDefinitionId = docId,
                IsEnabled = true,
                IsRequiredByDefault = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        [Test]
        public async Task GetActiveAsync_Returns_Only_Active()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            await SeedAsync(db, 1, "A", "AA", "AAA", true);
            await SeedAsync(db, 2, "B", "BB", "BBB", false);

            var repo = new CountryRepository(db);
            var list = await repo.GetActiveAsync();

            list.Select(x => x.Name).Should().Contain("A").And.NotContain("B");
        }

        [Test]
        public async Task GetById_And_GetByIso_Work()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            await SeedAsync(db, 5, "France", "FR", "FRA");

            var repo = new CountryRepository(db);
            (await repo.GetByIdAsync(5))!.IsoCodeAlpha2.Should().Be("FR");
            (await repo.GetByIsoAsync("FR"))!.Name.Should().Be("France");
            (await repo.GetByIsoAsync("FRA"))!.Name.Should().Be("France");
            (await repo.GetByIsoAsync("fr"))!.Name.Should().Be("France"); // service uppercases, repo expects upper here but test ensures behavior works if upper given
        }

        [Test]
        public async Task Uniqueness_Checks_Work()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            await SeedAsync(db, 1, "Spain", "ES", "ESP");

            var repo = new CountryRepository(db);
            (await repo.NameExistsAsync("Spain")).Should().BeTrue();
            (await repo.Alpha2ExistsAsync("ES")).Should().BeTrue();
            (await repo.Alpha3ExistsAsync("ESP")).Should().BeTrue();

            // Except same id
            (await repo.NameExistsAsync("Spain", 1)).Should().BeFalse();
            (await repo.Alpha2ExistsAsync("ES", 1)).Should().BeFalse();
            (await repo.Alpha3ExistsAsync("ESP", 1)).Should().BeFalse();
        }

        [Test]
        public async Task Add_And_Save_Persists()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var repo = new CountryRepository(db);

            var c = new Country
            {
                Name = "Italy",
                IsoCodeAlpha2 = "IT",
                IsoCodeAlpha3 = "ITA",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            c.DocumentTypes = new List<DocumentType> { new DocumentType { Id = 7, Name = "ID", Code = "ID" } };

            await repo.AddAsync(c);
            await repo.SaveChangesAsync();

            var found = await repo.GetByIsoAsync("IT");
            found.Should().NotBeNull();
            found!.Name.Should().Be("Italy");
            found.DocumentTypes.Should().ContainSingle(d => d.Code == "ID");
        }

        [Test]
        public async Task GetAllListItemsAsync_Returns_Mapped_Counts()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);

            var fr = await SeedAsync(db, 1, "France", "FR", "FRA");
            var de = await SeedAsync(db, 2, "Germany", "DE", "DEU");
            // two mappings for FR, one for DE
            await SeedMappingAsync(db, fr.Id, Guid.NewGuid());
            await SeedMappingAsync(db, fr.Id, Guid.NewGuid());
            await SeedMappingAsync(db, de.Id, Guid.NewGuid());

            var repo = new CountryRepository(db);
            var rows = await repo.GetAllListItemsAsync();

            rows.Should().Contain(x => x.Name == "France" && x.MappedCount == 2);
            rows.Should().Contain(x => x.Name == "Germany" && x.MappedCount == 1);
        }
    }
}
