using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Data;
using IDV_Backend.Models.QRCode;
using IDV_Backend.Repositories.QRCodes;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace UserTest.Repositories.QRCodes
{
    [TestFixture]
    public class QRCodeRepositoryTests
    {
        private ApplicationDbContext _db = default!;
        private IQRCodeRepository _repo = default!;

        [SetUp]
        public void SetUp()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
                .Options;

            _db = new ApplicationDbContext(opts);
            _repo = new QRCodeRepository(_db);
        }

        [TearDown]
        public void TearDown() => _db.Dispose();

        [Test]
        public async Task Add_List_Get_Remove_Roundtrip_Works()
        {
            var e = new QRCode
            {
                GeneratedCode = "R1",
                ExpiryTime = System.DateTime.UtcNow.AddMinutes(5),
                SectionResponseMappingId = 123
            };

            await _repo.AddAsync(e, CancellationToken.None);
            await _repo.SaveChangesAsync(CancellationToken.None);

            // List
            var list = await _repo.ListAsync(CancellationToken.None);
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list[0].GeneratedCode, Is.EqualTo("R1"));

            // GetById
            var byId = await _repo.GetByIdAsync(list[0].QrId, CancellationToken.None);
            Assert.That(byId, Is.Not.Null);
            Assert.That(byId!.GeneratedCode, Is.EqualTo("R1"));

            // Remove (delete the tracked instance to avoid EF tracking conflict)
            await _repo.RemoveAsync(e, CancellationToken.None);
            await _repo.SaveChangesAsync(CancellationToken.None);

            var after = await _repo.GetByIdAsync(list[0].QrId, CancellationToken.None);
            Assert.That(after, Is.Null);
        }
    }
}
