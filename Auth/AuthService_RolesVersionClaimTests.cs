using IDV_Backend.Contracts.Auth;
using IDV_Backend.Models;
using IDV_Backend.Models.User;
using IDV_Backend.Repositories.Auth;
using IDV_Backend.Repositories.Roles;
using IDV_Backend.Repositories.Users;
using IDV_Backend.Services.Auth;
using IDV_Backend.Services.Roles;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UserTest.Auth;

public class AuthService_RolesVersionClaimTests
{
    private class InMemoryRefreshRepo : IRefreshTokenRepository
    {
        private readonly List<RefreshToken> _tokens = new();
        public Task AddAsync(RefreshToken token, CancellationToken ct = default)
        {
            _tokens.Add(token);
            return Task.CompletedTask;
        }
        public Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default) =>
            Task.FromResult(_tokens.FirstOrDefault(t => t.Token == token));
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    [Test]
    public async Task Login_Issues_Token_With_RolesVersion_Claim()
    {
        // Arrange
        var users = new Mock<IUserRepository>();
        var refresh = new InMemoryRefreshRepo();
        var roles = new Mock<IRoleRepository>();
        var perms = new Mock<IPermissionRepository>();
        var rolesVersion = new Mock<IRolesVersionService>();

        rolesVersion.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);

        var user = new User
        {
            Id = 123,
            Email = "test@example.com",
            FirstName = "T",
            LastName = "E",
            RoleId = 2,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass"),
            PublicId = 999,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        users.Setup(u => u.GetByEmailWithRoleNoTrackingAsync("test@example.com", It.IsAny<CancellationToken>()))
             .ReturnsAsync(user);
        users.Setup(u => u.GetRoleNameByIdAsync(2, It.IsAny<CancellationToken>()))
             .ReturnsAsync("User");

        perms.Setup(p => p.GetCodesByRoleIdAsync(2, It.IsAny<CancellationToken>()))
             .ReturnsAsync(Array.Empty<string>());

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience",
            ["Jwt:Key"] = "01234567890123456789012345678901",
            ["Jwt:AccessTokenMinutes"] = "60",
            ["Jwt:RefreshTokenDays"] = "7"
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings!)
            .Build();

        var svc = new AuthService(
            users.Object,
            refresh,
            roles.Object,
            perms.Object,
            rolesVersion.Object,
            config);

        // Act
        var resp = await svc.LoginAsync(new LoginRequest("test@example.com", "pass"), CancellationToken.None);

        // Assert
        Assert.That(resp.AccessToken, Is.Not.Null.And.Not.Empty);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(resp.AccessToken);
        var rolesVerClaim = jwt.Claims.FirstOrDefault(c => c.Type == "roles_ver");
        Assert.That(rolesVerClaim, Is.Not.Null);
        Assert.That(rolesVerClaim!.Value, Is.EqualTo("7"));
    }
}
