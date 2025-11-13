using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Models.User;
using IDV_Backend.Repositories.Users;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UserTest.Repositories.Users
{
    [TestFixture]
    public class UserRepositoryTests
    {
        private ApplicationDbContext _db = default!;
        private IUserRepository _repo = default!;

        [SetUp]
        public void SetUp()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
                .Options;

            _db = new ApplicationDbContext(opts);

            // Seed a role we can use in tests
            _db.Roles.Add(new RoleUserMapping { Id = 1, RoleName = RoleName.User });
            _db.Roles.Add(new RoleUserMapping { Id = 2, RoleName = RoleName.Admin });
            _db.SaveChanges();

            _repo = new UserRepository(_db);
        }

        [TearDown]
        public void TearDown() => _db.Dispose();

        [Test]
        public async Task AddUser_And_GetById_WithRole_Works()
        {
            var user = new User
            {
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada@idv.local",
                Phone = "000",
                RoleId = 1,
                ClientReferenceId = 111,
                PublicId = 222,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng@Pass")
            };

            await _repo.AddUserAsync(user, CancellationToken.None);
            await _repo.SaveChangesAsync(CancellationToken.None);

            var fetched = await _repo.GetByIdWithRoleNoTrackingAsync(user.Id, CancellationToken.None);
            Assert.That(fetched, Is.Not.Null);
            Assert.That(fetched!.Email, Is.EqualTo("ada@idv.local"));
            Assert.That(fetched.Role, Is.Not.Null);
            Assert.That(fetched.Role!.RoleName, Is.EqualTo(RoleName.User));
        }

        [Test]
        public async Task EmailExistsWithRole_IsCaseInsensitive_And_TrueForExisting()
        {
            var user = new User
            {
                FirstName = "Alan",
                LastName = "Turing",
                Email = "Alan@IDV.local",
                RoleId = 1,
                ClientReferenceId = 333,
                PublicId = 444,
                PasswordHash = "hash"
            };
            await _repo.AddUserAsync(user, CancellationToken.None);
            await _repo.SaveChangesAsync(CancellationToken.None);

            var exists = await _repo.EmailExistsWithRoleAsync("alan@idv.local", CancellationToken.None);
            var notExists = await _repo.EmailExistsWithRoleAsync("missing@idv.local", CancellationToken.None);

            Assert.That(exists, Is.True);
            Assert.That(notExists, Is.False);
        }

        [Test]
        public async Task GetRoleNameById_Returns_Name()
        {
            var roleName = await _repo.GetRoleNameByIdAsync(2, CancellationToken.None);
            Assert.That(roleName, Is.EqualTo("Admin"));
        }

        [Test]
        public async Task GetByEmailWithRoleNoTracking_Works()
        {
            var u = new User
            {
                FirstName = "Grace",
                LastName = "Hopper",
                Email = "grace@idv.local",
                RoleId = 2,
                ClientReferenceId = 10,
                PublicId = 20,
                PasswordHash = "hash"
            };
            await _repo.AddUserAsync(u, CancellationToken.None);
            await _repo.SaveChangesAsync(CancellationToken.None);

            var got = await _repo.GetByEmailWithRoleNoTrackingAsync("grace@idv.local", CancellationToken.None);
            Assert.That(got, Is.Not.Null);
            Assert.That(got!.Role, Is.Not.Null);
            Assert.That(got.Role!.RoleName, Is.EqualTo(RoleName.Admin));
        }

        [Test]
        public async Task GetAllWithRoleNoTracking_Returns_All()
        {
            var u1 = new User { FirstName = "A", LastName = "A", Email = "a@idv.local", RoleId = 1, ClientReferenceId = 1, PublicId = 1, PasswordHash = "h" };
            var u2 = new User { FirstName = "B", LastName = "B", Email = "b@idv.local", RoleId = 2, ClientReferenceId = 2, PublicId = 2, PasswordHash = "h" };
            await _repo.AddUserAsync(u1, CancellationToken.None);
            await _repo.AddUserAsync(u2, CancellationToken.None);
            await _repo.SaveChangesAsync(CancellationToken.None);

            var all = await _repo.GetAllWithRoleNoTrackingAsync(false, CancellationToken.None);
            Assert.That(all.Count, Is.EqualTo(2));
            Assert.That(all.Select(x => x.Email).ToList(), Is.EquivalentTo(new[] { "a@idv.local", "b@idv.local" }));
            Assert.That(all.All(x => x.Role != null), Is.True);
        }

        [Test]
        public async Task GetByIdTracked_Then_Save_Via_Context_Works()
        {
            var u = new User { FirstName = "E", LastName = "C", Email = "ec@idv.local", RoleId = 1, ClientReferenceId = 5, PublicId = 6, PasswordHash = "h" };
            await _repo.AddUserAsync(u, CancellationToken.None);
            await _repo.SaveChangesAsync(CancellationToken.None);

            var tracked = await _repo.GetByIdTrackedAsync(u.Id, CancellationToken.None);
            Assert.That(tracked, Is.Not.Null);

            tracked!.Phone = "123";
            await _repo.SaveChangesAsync(CancellationToken.None);

            var again = await _repo.GetByIdWithRoleNoTrackingAsync(u.Id, CancellationToken.None);
            Assert.That(again!.Phone, Is.EqualTo("123"));
        }

        [Test]
        public async Task RemoveByIdAsync_Deletes_User()
        {
            var u = new User { FirstName = "Del", LastName = "User", Email = "del@idv.local", RoleId = 1, ClientReferenceId = 7, PublicId = 8, PasswordHash = "h" };
            await _repo.AddUserAsync(u, CancellationToken.None);
            await _repo.SaveChangesAsync(CancellationToken.None);

            var ok = await _repo.RemoveByIdAsync(u.Id, CancellationToken.None);
            Assert.That(ok, Is.True);

            var missing = await _repo.GetByIdWithRoleNoTrackingAsync(u.Id, CancellationToken.None);
            Assert.That(missing, Is.Null);
        }
    }
}
