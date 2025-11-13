using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IDV_Backend.Data;
using IDV_Backend.Models.Countries;
using IDV_Backend.Models.CountryDocumentDefinitionMappings;
using IDV_Backend.Repositories.Countries;
using IDV_Backend.Repositories.CountryDocumentDefinitionMappings;
using IDV_Backend.Services.CountryDocumentDefinitionMappings;
using IDV_Backend.Services.Countries;
using IDV_Backend.Contracts.Countries;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Services.Countries
{
    public sealed class CountryServiceTests
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

        private static Country NewCountry(string name, string iso2, string iso3, bool active = true, IEnumerable<DocumentType>? types = null)
        {
            var c = new Country
            {
                Name = name,
                IsoCodeAlpha2 = iso2,
                IsoCodeAlpha3 = iso3,
                IsActive = active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            c.DocumentTypes = types?.ToList() ?? new List<DocumentType> { new DocumentType { Id = 1, Name = "Passport", Code = "PASS" } };
            return c;
        }

        private sealed class FakeMappingService : ICountryDocumentDefinitionMappingService
        {
            private readonly Dictionary<int, List<CountryDocDefReadModel>> _data = new();

            public void Seed(int countryId, IEnumerable<CountryDocDefReadModel> rows)
                => _data[countryId] = rows.ToList();

            public Task<IReadOnlyList<CountryDocDefReadModel>> GetMappingsWithDocDetailsAsync(
                int countryId, bool onlyEnabled = true, System.Threading.CancellationToken cancellationToken = default)
            {
                _data.TryGetValue(countryId, out var list);
                return Task.FromResult<IReadOnlyList<CountryDocDefReadModel>>(list ?? new List<CountryDocDefReadModel>());
            }
        }

        private sealed class FakeMappingRepo : ICountryDocumentDefinitionMappingRepository
        {
            private readonly List<CountryDocumentDefinitionMapping> _rows = new();

            public void Seed(IEnumerable<CountryDocumentDefinitionMapping> rows)
            {
                _rows.Clear();
                _rows.AddRange(rows);
            }

            public Task<List<CountryDocumentDefinitionMapping>> GetByCountryIdAsync(int countryId, bool onlyEnabled = true, System.Threading.CancellationToken cancellationToken = default)
                => Task.FromResult(_rows.Where(r => r.CountryId == countryId && (!onlyEnabled || r.IsEnabled)).ToList());

            public Task<List<CountryDocumentDefinitionMapping>> GetAllAsync(bool onlyEnabled = true, System.Threading.CancellationToken cancellationToken = default)
                => Task.FromResult(_rows.Where(r => !onlyEnabled || r.IsEnabled).ToList());

            public Task<bool> ExistsAsync(int countryId, Guid docDefId, System.Threading.CancellationToken ct = default) => Task.FromResult(false);
            public Task AddAsync(CountryDocumentDefinitionMapping row, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
            public Task<int> RemoveAsync(int countryId, Guid docDefId, System.Threading.CancellationToken ct = default) => Task.FromResult(0);
            public Task ReplaceAllAsync(int countryId, IReadOnlyCollection<CountryDocumentDefinitionMapping> newRows, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        }

        [Test]
        public async Task CreateCountryAsync_Validates_And_Persists()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var repo = new CountryRepository(db);
            var fakeMapRepo = new FakeMappingRepo();
            var fakeMapSvc = new FakeMappingService();

            var svc = new CountryService(repo, fakeMapRepo, fakeMapSvc);

            var dto = new CreateCountryDto
            {
                Name = "Canada",
                IsoCodeAlpha2 = "CA",
                IsoCodeAlpha3 = "CAN",
                DocumentTypes = new List<DocumentTypeDto>
                {
                    new DocumentTypeDto { Id = 1, Name = "Passport", Code = "PASS" }
                },
                IsActive = true
            };

            var created = await svc.CreateCountryAsync(dto);
            created.Id.Should().BeGreaterThan(0);
            created.Name.Should().Be("Canada");

            // Verify it really saved
            var fromDb = await repo.GetByIsoAsync("CA");
            fromDb.Should().NotBeNull();
            fromDb!.DocumentTypes.Should().ContainSingle(d => d.Code == "PASS");
        }

        [Test]
        public async Task UpdateCountryAsync_Changes_Name_And_Ensures_Uniqueness()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);

            // seed two countries
            var c1 = NewCountry("Spain", "ES", "ESP");
            var c2 = NewCountry("Portugal", "PT", "PRT");
            db.Countries.AddRange(c1, c2);
            await db.SaveChangesAsync();

            var repo = new CountryRepository(db);
            var svc = new CountryService(repo, new FakeMappingRepo(), new FakeMappingService());

            // happy path rename
            var updated = await svc.UpdateCountryAsync(c1.Id, new UpdateCountryDto { Name = "España" });
            updated!.Name.Should().Be("España");

            // conflict name
            Func<Task> act = () => svc.UpdateCountryAsync(c1.Id, new UpdateCountryDto { Name = "Portugal" });
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*already exists*");
        }

        [Test]
        public async Task GetAllAsync_Returns_Mapped_Counts()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var repo = new CountryRepository(db);

            // seed country + mappings in DbContext (GetAllListItemsAsync counts via DbContext)
            var fr = NewCountry("France", "FR", "FRA");
            db.Countries.Add(fr);
            await db.SaveChangesAsync();

            // add two mappings (enabled)
            db.CountryDocumentDefinitionMappings.AddRange(
                new CountryDocumentDefinitionMapping { CountryId = fr.Id, DocumentDefinitionId = Guid.NewGuid(), IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new CountryDocumentDefinitionMapping { CountryId = fr.Id, DocumentDefinitionId = Guid.NewGuid(), IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();

            var svc = new CountryService(repo, new FakeMappingRepo(), new FakeMappingService());
            var items = await svc.GetAllAsync();

            items.Should().ContainSingle(x => x.Name == "France" && x.MappedDocumentsCount == 2);
        }

        [Test]
        public async Task GetByIdAsync_Projects_Mapped_Docs_From_Service()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);

            var fr = NewCountry("France", "FR", "FRA");
            db.Countries.Add(fr);
            await db.SaveChangesAsync();

            var repo = new CountryRepository(db);
            var fakeSvc = new FakeMappingService();
            fakeSvc.Seed(fr.Id, new[]
            {
                new IDV_Backend.Services.CountryDocumentDefinitionMappings.CountryDocDefReadModel(
                    CountryId: fr.Id,
                    CountryIso2: "FR",
                    DocumentDefinitionId: Guid.NewGuid(),
                    IsEnabled: true,
                    IsRequiredByDefault: true,
                    Title: "Passport",
                    Category: "Passport",
                    Status: "Published",
                    IsLatest: true,
                    Version: 1,
                    DocumentCountryIso2: "FR")
            });

            var svc = new CountryService(repo, new FakeMappingRepo(), fakeSvc);
            var dto = await svc.GetByIdAsync(fr.Id);

            dto.Should().NotBeNull();
            dto!.MappedDocuments.Should().HaveCount(1);
            dto.MappedDocuments[0].Title.Should().Be("Passport");
        }

        [Test]
        public async Task DeactivateCountryAsync_Soft_Deletes()
        {
            var dbName = Guid.NewGuid().ToString();
            await using var db = NewDb(dbName);
            var c = NewCountry("Italy", "IT", "ITA");
            db.Countries.Add(c);
            await db.SaveChangesAsync();

            var repo = new CountryRepository(db);
            var svc = new CountryService(repo, new FakeMappingRepo(), new FakeMappingService());

            var ok = await svc.DeactivateCountryAsync(c.Id);
            ok.Should().BeTrue();

            var fromDb = await repo.GetByIdAsync(c.Id);
            fromDb!.IsActive.Should().BeFalse();
        }
    }
}
