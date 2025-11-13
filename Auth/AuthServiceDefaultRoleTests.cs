// UserTest/Services/Auth/AuthServiceDefaultRoleTests.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    public class AuthServiceDefaultRoleTests
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
        public async Task Register_Uses_DefaultRoleId_From_Config()
        {
            // Arrange DB
            _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(System.Guid.NewGuid().ToString()).Options);

            // role id 3 == User (legacy default)
            _db.Roles.Add(new RoleUserMapping { Id = 3, RoleName = RoleName.User });
            _db.SaveChanges();

            _users = new UserRepository(_db);
            _refresh = new RefreshTokenRepository(_db);
            _roles = new RoleRepository(_db);

            _cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "t",
                    ["Jwt:Audience"] = "t",
                    ["Jwt:Key"] = "0123456789ABCDEF0123456789ABCDEF",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "7",
                    ["Auth:DefaultRoleId"] = "3"
                })
                .Build();

            // Strict mock that ALLOWS the IssueTokensAsync call:
            _perms = new Mock<IPermissionRepository>(MockBehavior.Strict);
            _perms.Setup(p => p.GetCodesByRoleIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<string>()); // no perms needed for this test

            _rolesVersion = new Mock<IRolesVersionService>();

            var svc = new AuthService(_users, _refresh, _roles, _perms.Object, _rolesVersion.Object, _cfg);

            // Act
            var res = await svc.RegisterAsync(new RegisterRequest("A", "B", "u@x.com", null, "Str0ng@Pass"));

            // Assert
            Assert.That(res.UserId, Is.GreaterThan(0));
            Assert.That(res.Role, Is.EqualTo("User"));
            _perms.VerifyAll();
        }

        [Test]
        public async Task Register_FallsBack_To_DefaultRoleName_When_Id_Missing()
        {
            // Arrange DB
            _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(System.Guid.NewGuid().ToString()).Options);

            // Seed only the name-based role
            _db.Roles.Add(new RoleUserMapping { Id = 3, RoleName = RoleName.User });
            _db.SaveChanges();

            _users = new UserRepository(_db);
            _refresh = new RefreshTokenRepository(_db);
            _roles = new RoleRepository(_db);

            _cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "t",
                    ["Jwt:Audience"] = "t",
                    ["Jwt:Key"] = "0123456789ABCDEF0123456789ABCDEF",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "7",
                    // NO Auth:DefaultRoleId -> force fallback
                    ["Auth:DefaultRoleName"] = "User"
                })
                .Build();

            _perms = new Mock<IPermissionRepository>(MockBehavior.Strict);
            _perms.Setup(p => p.GetCodesByRoleIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<string>());

            _rolesVersion = new Mock<IRolesVersionService>();

            var svc = new AuthService(_users, _refresh, _roles, _perms.Object, _rolesVersion.Object, _cfg);

            // Act
            var res = await svc.RegisterAsync(new RegisterRequest("A", "B", "v@x.com", null, "Str0ng@Pass"));

            // Assert
            Assert.That(res.UserId, Is.GreaterThan(0));
            Assert.That(res.Role, Is.EqualTo("User"));
            _perms.VerifyAll();
        }
    }
}
