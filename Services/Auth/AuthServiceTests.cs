using System;
using System.Threading;
using IDV_Backend.Contracts.Auth;
using IDV_Backend.Data;
using IDV_Backend.Models;
using IDV_Backend.Repositories.Auth;
using IDV_Backend.Repositories.Roles;
using IDV_Backend.Repositories.Users;
using IDV_Backend.Services.Auth;
using IDV_Backend.Services.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;

namespace UserTest.Services.Auth
{
    [TestFixture]
    public class AuthServiceTests
    {
        private ApplicationDbContext _db = default!;
        private IUserRepository _users = default!;
        private IRefreshTokenRepository _refresh = default!;
        private IAuthService _svc = default!;
        private IConfiguration _cfg = default!;
        private IRoleRepository _roles = default!;
        private IPermissionRepository _perms = default!;
        private IRolesVersionService _rolesVersion = default!;

        [SetUp]
        public void SetUp()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
                .Options;
            _db = new ApplicationDbContext(opts);

            // Seed role referenced by Auth:DefaultRoleId
            _db.Roles.Add(new RoleUserMapping { Id = 99, RoleName = RoleName.User });
            _db.SaveChanges();

            _users = new UserRepository(_db);
            _refresh = new RefreshTokenRepository(_db);

            // Keep role repo as a simple mock (not used in this test flow)
            var rolesMock = new Mock<IRoleRepository>();
            _roles = rolesMock.Object;

            // Ensure permissions repo returns an empty list instead of null
            var permsMock = new Mock<IPermissionRepository>();
            permsMock
                .Setup(p => p.GetCodesByRoleIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<string>());
            _perms = permsMock.Object;

            // Provide a concrete roles-version return value to avoid unexpected nulls
            var rolesVerMock = new Mock<IRolesVersionService>();
            rolesVerMock
                .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            _rolesVersion = rolesVerMock.Object;

            _cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "test-issuer",
                    ["Jwt:Audience"] = "test-audience",
                    ["Jwt:Key"] = "0123456789ABCDEF0123456789ABCDEF",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "7",
                    ["Auth:DefaultRoleId"] = "99"
                })
                .Build();

            _svc = new AuthService(_users, _refresh, _roles, _perms, _rolesVersion, _cfg);
        }

        [TearDown]
        public void TearDown() => _db.Dispose();

        [Test]
        public async Task Register_Login_Refresh_Logout_Roundtrip_Works()
        {
            var reg = await _svc.RegisterAsync(new RegisterRequest(
                FirstName: "Ada",
                LastName: "Lovelace",
                Email: "Ada@IDV.local",
                Phone: "123",
                Password: "Str0ng@Pass"));

            Assert.That(reg.UserId, Is.GreaterThan(0));
            Assert.That(reg.AccessToken, Is.Not.Empty);
            Assert.That(reg.RefreshToken, Is.Not.Empty);
            Assert.That(reg.Role, Is.EqualTo("User"));

            var login = await _svc.LoginAsync(new LoginRequest("ada@idv.local", "Str0ng@Pass"));
            Assert.That(login.AccessToken, Is.Not.Empty);

            var refreshed = await _svc.RefreshAsync(new RefreshRequest(login.RefreshToken));
            Assert.That(refreshed.RefreshToken, Is.Not.EqualTo(login.RefreshToken));

            var ok = await _svc.LogoutAsync(refreshed.RefreshToken);
            Assert.That(ok, Is.True);
        }

        [Test]
        public void Login_InvalidCreds_Fails()
        {
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await _svc.LoginAsync(new LoginRequest("missing@idv.local", "nope")));
        }

        [Test]
        public async Task Refresh_InvalidToken_Fails()
        {
            // Seed a user so DB isn't empty
            await _svc.RegisterAsync(new RegisterRequest("A", "B", "x@idv.local", null, "Str0ng@Pass"));

            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await _svc.RefreshAsync(new RefreshRequest("not-a-token")));
        }
    }
}
