using IDV_Backend.Data;
using IDV_Backend.Models.VerificationResults;
using IDV_Backend.Repositories.VerificationResults;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
// If your solution already has the real model, you don't need this.
// using IDV_Backend.Models.UserTemplateSubmissions;

namespace UserTest.Repositories.VerificationResults
{
    public sealed class VerificationResultRepositoryTests : IDisposable
    {
        private readonly ApplicationDbContext _ctx;
        private readonly VerificationResultRepository _repo;

        public VerificationResultRepositoryTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"vr_repo_tests_{Guid.NewGuid()}")
                .EnableSensitiveDataLogging()
                .Options;

            _ctx = new ApplicationDbContext(options);

            // Seed a couple of UserTemplateSubmissions for FK existence checks
            _ctx.Set<IDV_Backend.Models.UserTemplateSubmissions.UserTemplateSubmission>().AddRange(
                new IDV_Backend.Models.UserTemplateSubmissions.UserTemplateSubmission { Id = 101, IsDeleted = false },
                new IDV_Backend.Models.UserTemplateSubmissions.UserTemplateSubmission { Id = 102, IsDeleted = true } // deleted
            );
            _ctx.SaveChanges();

            _repo = new VerificationResultRepository(_ctx);
        }

        [Fact]
        public async Task SubmissionExistsAsync_Works_ForExistingAndDeleted()
        {
            Assert.True(await _repo.SubmissionExistsAsync(101));
            Assert.False(await _repo.SubmissionExistsAsync(102)); // deleted
            Assert.False(await _repo.SubmissionExistsAsync(999));
        }

        [Fact]
        public async Task Add_And_GetById_Works()
        {
            var vr = new VerificationResult
            {
                Id = Guid.NewGuid(),
                UserTemplateSubmissionId = 101,
                ManualStatus = ManualVerificationStatus.NotVerified
            };
            await _repo.AddAsync(vr);
            await _repo.SaveChangesAsync();

            var fetched = await _repo.GetByIdAsync(vr.Id);
            Assert.NotNull(fetched);
            Assert.Equal(101, fetched!.UserTemplateSubmissionId);
        }

        [Fact]
        public async Task SearchAsync_Filters_Sorts_And_Paginates()
        {
            var now = DateTime.UtcNow;

            _ctx.Set<VerificationResult>().AddRange(
                new VerificationResult { Id = Guid.NewGuid(), UserTemplateSubmissionId = 101, ManualStatus = ManualVerificationStatus.Verified, AutoConfidenceScore = 0.9, CreatedAt = now.AddMinutes(-10), UpdatedAt = now.AddMinutes(-9) },
                new VerificationResult { Id = Guid.NewGuid(), UserTemplateSubmissionId = 101, ManualStatus = ManualVerificationStatus.Rejected, AutoConfidenceScore = 0.4, CreatedAt = now.AddMinutes(-8), UpdatedAt = now.AddMinutes(-7) },
                new VerificationResult { Id = Guid.NewGuid(), UserTemplateSubmissionId = 101, ManualStatus = ManualVerificationStatus.Verified, AutoConfidenceScore = 0.7, CreatedAt = now.AddMinutes(-6), UpdatedAt = now.AddMinutes(-5), IsDeleted = true },
                new VerificationResult { Id = Guid.NewGuid(), UserTemplateSubmissionId = 999, ManualStatus = ManualVerificationStatus.Verified, AutoConfidenceScore = 0.1, CreatedAt = now.AddMinutes(-4), UpdatedAt = now.AddMinutes(-3) }
            );
            await _ctx.SaveChangesAsync();

            var req = new IDV_Backend.Contracts.VerificationResults.SearchVerificationResultsRequest
            {
                Page = 1,
                PageSize = 2,
                UserTemplateSubmissionId = 101,
                ManualStatus = ManualVerificationStatus.Verified,
                SortBy = "createdAt",
                SortDir = "desc"
            };

            var (items, total) = await _repo.SearchAsync(req, CancellationToken.None);
            Assert.Equal(1, items.Count); // one deleted is ignored
            Assert.Equal(1, total);
            Assert.True(items.All(x => x.UserTemplateSubmissionId == 101));
            Assert.True(items.All(x => x.ManualStatus == ManualVerificationStatus.Verified));
        }

        [Fact]
        public async Task GetBySubmissionIdAsync_ReturnsDescendingByCreatedAt()
        {
            var now = DateTime.UtcNow;

            _ctx.Set<VerificationResult>().AddRange(
                new VerificationResult { Id = Guid.NewGuid(), UserTemplateSubmissionId = 101, CreatedAt = now.AddMinutes(-1) },
                new VerificationResult { Id = Guid.NewGuid(), UserTemplateSubmissionId = 101, CreatedAt = now.AddMinutes(-5) },
                new VerificationResult { Id = Guid.NewGuid(), UserTemplateSubmissionId = 101, CreatedAt = now.AddMinutes(-3), IsDeleted = true },
                new VerificationResult { Id = Guid.NewGuid(), UserTemplateSubmissionId = 999, CreatedAt = now.AddMinutes(-2) }
            );
            await _ctx.SaveChangesAsync();

            var list = await _repo.GetBySubmissionIdAsync(101, CancellationToken.None);
            Assert.Equal(2, list.Count);
            Assert.True(list[0].CreatedAt >= list[1].CreatedAt);
        }

        [Fact]
        public async Task SoftDeleteAsync_SetsFlag_AndHonorsRowVersion()
        {
            var id = Guid.NewGuid();
            var vr = new VerificationResult
            {
                Id = id,
                UserTemplateSubmissionId = 101,
                Notes = "x"
            };
            _ctx.Set<VerificationResult>().Add(vr);
            await _ctx.SaveChangesAsync();

            // fetch again to get the RowVersion value EF sets
            var existing = await _repo.GetByIdAsync(id);
            Assert.NotNull(existing);
            var row = existing!.RowVersion;

            // correct row version -> success
            var ok = await _repo.SoftDeleteAsync(id, row, CancellationToken.None);
            Assert.True(ok);

            // already deleted -> not found
            var ok2 = await _repo.SoftDeleteAsync(id, row, CancellationToken.None);
            Assert.False(ok2);
        }

        public void Dispose()
        {
            _ctx?.Dispose();
        }
    }
}
