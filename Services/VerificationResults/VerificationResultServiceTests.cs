using IDV_Backend.Contracts.VerificationResults;
using IDV_Backend.Models.VerificationResults;
using IDV_Backend.Repositories.VerificationResults;
using IDV_Backend.Services.VerificationResults;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace UserTest.Services.VerificationResults
{
    public sealed class VerificationResultServiceTests
    {
        private readonly Mock<IVerificationResultRepository> _repo = new();

        private VerificationResultService CreateSut() => new(_repo.Object);

        [Fact]
        public async Task CreateAsync_ValidatesSubmission_AddsAndReturnsResponse()
        {
            var sut = CreateSut();

            _repo.Setup(r => r.SubmissionExistsAsync(111, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            VerificationResult? added = null;
            _repo.Setup(r => r.AddAsync(It.IsAny<VerificationResult>(), It.IsAny<CancellationToken>()))
                 .Callback<VerificationResult, CancellationToken>((e, _) => added = e)
                 .Returns(Task.CompletedTask);
            _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var req = new CreateVerificationResultRequest
            {
                UserTemplateSubmissionId = 111,
                AutoConfidenceScore = 0.77,
                ManualStatus = ManualVerificationStatus.RequiresReview,
                Notes = "init"
            };

            var resp = await sut.CreateAsync(req, CancellationToken.None);

            Assert.NotNull(resp);
            Assert.NotNull(added);
            Assert.Equal(111, resp.UserTemplateSubmissionId);
            Assert.Equal(ManualVerificationStatus.RequiresReview, resp.ManualStatus);
            Assert.Equal(0.77, resp.AutoConfidenceScore);
            _repo.Verify(r => r.AddAsync(It.IsAny<VerificationResult>(), It.IsAny<CancellationToken>()), Times.Once);
            _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_Throws_WhenSubmissionMissing()
        {
            var sut = CreateSut();
            _repo.Setup(r => r.SubmissionExistsAsync(222, It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var req = new CreateVerificationResultRequest { UserTemplateSubmissionId = 222 };
            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateAsync(req, CancellationToken.None));
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
        {
            var sut = CreateSut();
            _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((VerificationResult?)null);

            var res = await sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);
            Assert.Null(res);
        }

        [Fact]
        public async Task SearchAsync_MapsPagedResults()
        {
            var sut = CreateSut();
            var list = new List<VerificationResult>
            {
                new VerificationResult { Id = Guid.NewGuid(), UserTemplateSubmissionId = 9, ManualStatus = ManualVerificationStatus.Verified }
            };

            _repo.Setup(r => r.SearchAsync(It.IsAny<SearchVerificationResultsRequest>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((list, 1));

            var resp = await sut.SearchAsync(new SearchVerificationResultsRequest { Page = 1, PageSize = 10 }, CancellationToken.None);
            Assert.Equal(1, resp.TotalCount);
            Assert.Single(resp.Items);
            Assert.Equal(ManualVerificationStatus.Verified, resp.Items[0].ManualStatus);
        }

        [Fact]
        public async Task UpdateManualStatusAsync_ChecksConcurrency_AndSaves()
        {
            var sut = CreateSut();
            var id = Guid.NewGuid();
            var entity = new VerificationResult { Id = id, ManualStatus = ManualVerificationStatus.NotVerified, RowVersion = new byte[] { 1, 2, 3 } };
            _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var req = new UpdateManualStatusRequest
            {
                ManualStatus = ManualVerificationStatus.Verified,
                RowVersionBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 })
            };

            var resp = await sut.UpdateManualStatusAsync(id, req, CancellationToken.None);
            Assert.Equal(ManualVerificationStatus.Verified, resp.ManualStatus);
            _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateManualStatusAsync_Throws_OnConcurrencyMismatch()
        {
            var sut = CreateSut();
            var id = Guid.NewGuid();
            var entity = new VerificationResult { Id = id, RowVersion = new byte[] { 9, 9, 9 } };
            _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

            var req = new UpdateManualStatusRequest
            {
                ManualStatus = ManualVerificationStatus.Verified,
                RowVersionBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 })
            };

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => sut.UpdateManualStatusAsync(id, req, CancellationToken.None));
        }

        [Fact]
        public async Task UpdateNotesAsync_Works()
        {
            var sut = CreateSut();
            var id = Guid.NewGuid();
            var entity = new VerificationResult { Id = id, Notes = "old", RowVersion = null };
            _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var req = new UpdateNotesRequest { Notes = "new", RowVersionBase64 = null };
            var resp = await sut.UpdateNotesAsync(id, req, CancellationToken.None);

            Assert.Equal("new", resp.Notes);
            _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetByUserTemplateSubmissionIdAsync_MapsResponses()
        {
            var sut = CreateSut();
            var list = new List<VerificationResult>
            {
                new VerificationResult { Id = Guid.NewGuid(), UserTemplateSubmissionId = 321, CreatedAt = DateTime.UtcNow }
            };
            _repo.Setup(r => r.GetBySubmissionIdAsync(321, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(list);

            var res = await sut.GetByUserTemplateSubmissionIdAsync(321, CancellationToken.None);
            Assert.Single(res);
            Assert.Equal(321, res.First().UserTemplateSubmissionId);
        }

        [Fact]
        public async Task SoftDeleteAsync_ThrowsWhenNotFound()
        {
            var sut = CreateSut();
            _repo.Setup(r => r.SoftDeleteAsync(It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SoftDeleteAsync(Guid.NewGuid(), null, CancellationToken.None));
        }

        [Fact]
        public async Task SoftDeleteAsync_Succeeds()
        {
            var sut = CreateSut();
            _repo.Setup(r => r.SoftDeleteAsync(It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

            await sut.SoftDeleteAsync(Guid.NewGuid(), null, CancellationToken.None);
            _repo.Verify(r => r.SoftDeleteAsync(It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
