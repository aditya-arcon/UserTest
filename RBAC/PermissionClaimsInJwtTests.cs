// UserTest/Services/Auth/PermissionClaimsInJwtTests.cs
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
using NUnit.Framework.Legacy;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UserTest.Services.Auth
{
    [TestFixture]
    public class PermissionClaimsInJwtTests
    {
        private ApplicationDbContext _db = default!;
        private IUserRepository _users = default!;
        private IRefreshTokenRepository _refresh = default!;
        private IRoleRepository _roles = default!;
        private Mock<IPermissionRepository> _perms = default!;
        private IConfiguration _cfg = default!;
        private Mock<IRolesVersionService> _rolesVersion = default!;

        [TearDown] public void TearDown() => _db?.Dispose();

        [Test]
        public async Task Login_Issues_Permission_Claims_For_Role()
        {
            _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(System.Guid.NewGuid().ToString()).Options);

            // Seed role (Id=3 -> RoleName.User in production; id only matters for this test)
            _db.Roles.Add(new RoleUserMapping { Id = 3, RoleName = RoleName.User });
            _db.SaveChanges();

            _users = new UserRepository(_db);
            _refresh = new RefreshTokenRepository(_db);
            _roles = new RoleRepository(_db);

            _cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "issuer",
                    ["Jwt:Audience"] = "aud",
                    ["Jwt:Key"] = "0123456789ABCDEF0123456789ABCDEF",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "7",
                    ["Auth:DefaultRoleId"] = "3"
                })
                .Build();

            // The User role (id 3) has 2 permissions for this test
            var userPerms = new List<string> { "ViewRespondVerifications", "ManageSupportTickets" };

            _perms = new Mock<IPermissionRepository>(MockBehavior.Strict);
            _perms
                .Setup(p => p.GetCodesByRoleIdAsync(3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(userPerms);

            // STRICT roles-version mock: AuthService.IssueTokensAsync() calls GetAsync(...)
            _rolesVersion = new Mock<IRolesVersionService>(MockBehavior.Strict);
            _rolesVersion
                .Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1); // returns int, not long
            // Not strictly required here, but harmless if your AuthService ever calls it in future flows:
            _rolesVersion
                .Setup(s => s.BumpAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0));

            var svc = new AuthService(_users, _refresh, _roles, _perms.Object, _rolesVersion.Object, _cfg);

            // Register the user so creds are valid
            var email = "case@idv.local";
            var pass = "Str0ng@Pass";
            await svc.RegisterAsync(new RegisterRequest("Case", "User", email, null, pass));

            // Act: login
            var login = await svc.LoginAsync(new LoginRequest(email, pass));

            // Assert: token has the permission claims
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(login.AccessToken);
            var permsInToken = jwt.Claims.Where(c => c.Type == "perm").Select(c => c.Value).ToList();

            CollectionAssert.IsSupersetOf(permsInToken, userPerms);

            _perms.VerifyAll();
            _rolesVersion.Verify(s => s.GetAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }
    }
}
