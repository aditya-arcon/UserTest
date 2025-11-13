// Tests/Auth/AuthorizationSpecs.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using IDV_Backend.Constants;
using IDV_Backend.Contracts.Common;
using IDV_Backend.Contracts.Files;
using IDV_Backend.Contracts.SectionFieldMapping;
using IDV_Backend.Contracts.TemplateSection;
using IDV_Backend.Contracts.TemplatesLinkGenerations;
using IDV_Backend.Contracts.TemplateVersions;
using IDV_Backend.Contracts.Users;
using IDV_Backend.Controllers.Files;
using IDV_Backend.Controllers.Roles;
using IDV_Backend.Controllers.SectionFieldMapping;
using IDV_Backend.Controllers.TemplateSection;
using IDV_Backend.Controllers.TemplatesLinkGenerations;
using IDV_Backend.Controllers.TemplateVersions;
using IDV_Backend.Controllers.Users;
using IDV_Backend.Models.Files;
using IDV_Backend.Models.TemplateLogs;
using IDV_Backend.Services.AdminLogs;
using IDV_Backend.Services.Encryption;
using IDV_Backend.Services.Files;
using IDV_Backend.Services.Realtime;
using IDV_Backend.Services.SectionFieldMapping;
using IDV_Backend.Services.Storage;
using IDV_Backend.Services.TemplateLogs;
using IDV_Backend.Services.TemplatesLinkGenerations;
using IDV_Backend.Services.TemplateVersion;
using IDV_Backend.Services.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IDV_Backend.Tests.Auth
{
    #region Test host + auth plumbing

    // Fake auth handler -> authenticates only when "Authorization: Test test" OR any X-Test-* header is present.
    // X-Test-UserId: long
    // X-Test-Role: Admin
    // X-Test-Perms: CSV of permission codes (e.g., ConfigureRbac,ManageUsersAndRoles)
    public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public new const string Scheme = "Test";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder,
            ISystemClock clock)
            : base(options, logger, encoder, clock) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var hasXHeaders = Request.Headers.ContainsKey("X-Test-UserId")
                              || Request.Headers.ContainsKey("X-Test-Role")
                              || Request.Headers.ContainsKey("X-Test-Perms");

            var hasAuthHeader = Request.Headers.Authorization.Count > 0;
            if (!hasXHeaders && !hasAuthHeader)
                return Task.FromResult(AuthenticateResult.NoResult());

            if (hasAuthHeader)
            {
                var auth = Request.Headers.Authorization.ToString();
                if (!auth.StartsWith($"{Scheme} ", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>();

            var uid = Request.Headers.TryGetValue("X-Test-UserId", out var uidVal) && !string.IsNullOrWhiteSpace(uidVal)
                ? uidVal.ToString()
                : "1";
            claims.Add(new Claim(ClaimTypes.NameIdentifier, uid));
            claims.Add(new Claim(ClaimTypes.Name, $"testuser-{uid}"));

            var role = Request.Headers["X-Test-Role"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(role))
                claims.Add(new Claim(ClaimTypes.Role, role!));

            var perms = Request.Headers["X-Test-Perms"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(perms))
            {
                foreach (var p in perms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    claims.Add(new Claim("perm", p));
            }

            var id = new ClaimsIdentity(claims, Scheme);
            var principal = new ClaimsPrincipal(id);
            var ticket = new AuthenticationTicket(principal, Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    // Policy provider that understands:
    //  - RequireAdmin            => require role "Admin"
    //  - "Perm:XYZ"              => require claim "perm" == "XYZ"
    // For any other policy name (unknown/custom), treat it as "authenticated user" so unit tests don't depend
    // on the app's full production policy graph.
    public sealed class TestPolicyProvider : IAuthorizationPolicyProvider
    {
        private readonly DefaultAuthorizationPolicyProvider _fallback;
        public TestPolicyProvider(IOptions<AuthorizationOptions> options)
        {
            _fallback = new DefaultAuthorizationPolicyProvider(options);
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
        {
            var policy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes("Bearer", TestAuthHandler.Scheme)
                .RequireAuthenticatedUser()
                .Build();
            return Task.FromResult(policy);
        }

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
        {
            var policy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes("Bearer", TestAuthHandler.Scheme)
                .RequireAuthenticatedUser()
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (string.Equals(policyName, "RequireAdmin", StringComparison.OrdinalIgnoreCase))
            {
                var builder = new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes("Bearer", TestAuthHandler.Scheme)
                    .RequireAuthenticatedUser()
                    .RequireRole("Admin");
                return Task.FromResult<AuthorizationPolicy?>(builder.Build());
            }

            if (policyName.StartsWith("Perm:", StringComparison.OrdinalIgnoreCase))
            {
                var code = policyName.Substring("Perm:".Length);
                var builder = new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes("Bearer", TestAuthHandler.Scheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim("perm", code);
                return Task.FromResult<AuthorizationPolicy?>(builder.Build());
            }

            // IMPORTANT CHANGE:
            // For any unknown policy name, do NOT defer to the app's production-configured policies,
            // which may involve DB lookups / extra requirements. Just require authentication.
            var permissive = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes("Bearer", TestAuthHandler.Scheme)
                .RequireAuthenticatedUser()
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(permissive);
        }
    }

    // Minimal in-memory app host that loads only the controllers we want and swaps services to fakes.
    public sealed class ControllerOnlyFactory : WebApplicationFactory<UsersController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                // Auth: register ONLY the fake "Test" scheme and make it default.
                // Do NOT add another "Bearer" scheme here (the app already has it in production).
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });

                services.AddSingleton<IAuthorizationPolicyProvider, TestPolicyProvider>();

                // Add the controllers from the API assembly
                services.AddControllers()
                    .AddApplicationPart(typeof(UsersController).Assembly);

                // Stub services used by controllers
                services.AddSingleton<IUserService, StubUserService>();
                services.AddSingleton<IAdminActionLogger, StubAdminLogger>();
                services.AddSingleton<ITemplateVersionService, StubTemplateVersionService>();
                services.AddSingleton<ITemplateActionLogger, StubTemplateActionLogger>();
                services.AddSingleton<ISectionFieldMappingService, StubSectionFieldMappingService>();
                services.AddSingleton<IFileStorage, StubStorage>();
                services.AddSingleton<IFilesService, StubFilesService>();
                services.AddSingleton<IEncryptionService, StubEncryption>();
                services.AddSingleton<IHandoffNotifier, StubNotifier>();
                services.AddSingleton<ITemplatesLinkGenerationService, StubTemplatesLinkService>();
            });

            builder.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            });
        }

        public HttpClient CreateClientWith(string? role = null, string[]? perms = null, bool authenticated = true)
        {
            var client = CreateClient();
            if (authenticated)
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue(TestAuthHandler.Scheme, "test");
                client.DefaultRequestHeaders.Add("X-Test-UserId", "123");
                if (!string.IsNullOrWhiteSpace(role))
                    client.DefaultRequestHeaders.Add("X-Test-Role", role);
                if (perms is { Length: > 0 })
                    client.DefaultRequestHeaders.Add("X-Test-Perms", string.Join(",", perms));
            }
            return client;
        }
    }

    #endregion

    #region Stub implementations (match your current interfaces)

    sealed class StubAdminLogger : IAdminActionLogger
    {
        public Task<long> LogAsync(Models.AdminLogs.AdminAction action, Models.AdminLogs.ActionOutcome outcome, string? entityType = null, long? entityId = null, string? entityIdentifier = null, string? errorMessage = null, string? notes = null, string? metadata = null, CancellationToken ct = default)
            => Task.FromResult(1L);

        public Task<long> LogSuccessAsync(Models.AdminLogs.AdminAction action, string? entityType = null, long? entityId = null, string? entityIdentifier = null, string? notes = null, string? metadata = null, CancellationToken ct = default)
            => Task.FromResult(1L);

        public Task<long> LogFailureAsync(Models.AdminLogs.AdminAction action, string errorMessage, string? entityType = null, long? entityId = null, string? entityIdentifier = null, string? notes = null, string? metadata = null, CancellationToken ct = default)
            => Task.FromResult(1L);

        public Task<long> LogPartialSuccessAsync(Models.AdminLogs.AdminAction action, string? errorMessage = null, string? entityType = null, long? entityId = null, string? entityIdentifier = null, string? notes = null, string? metadata = null, CancellationToken ct = default)
            => Task.FromResult(1L);

        public Task<long> LogWithExplicitAdminAsync(long adminId, string adminRole, Models.AdminLogs.AdminAction action, Models.AdminLogs.ActionOutcome outcome, string? entityType = null, long? entityId = null, string? entityIdentifier = null, string? errorMessage = null, string? notes = null, string? metadata = null, CancellationToken ct = default)
            => Task.FromResult(1L);
    }

    sealed class StubTemplateActionLogger : ITemplateActionLogger
    {
        public Task<long> LogAsync(long templateId, string templateName, TemplateAction action, string changeSummary, long? versionId = null, string? versionNumber = null, object? changeDetails = null, string? entityType = null, long? entityId = null, CancellationToken ct = default)
            => Task.FromResult(1L);

        public Task<long> LogCreateAsync(long templateId, string templateName, string? additionalDetails = null, CancellationToken ct = default)
            => Task.FromResult(1L);

        public Task<long> LogUpdateAsync(long templateId, string templateName, string changeSummary, object? changeDetails = null, CancellationToken ct = default)
            => Task.FromResult(1L);

        public Task<long> LogDeleteAsync(long templateId, string templateName, string? reason = null, CancellationToken ct = default)
            => Task.FromResult(1L);

        public Task<long> LogVersionActionAsync(long templateId, string templateName, long versionId, string versionNumber, TemplateAction action, string changeSummary, object? changeDetails = null, CancellationToken ct = default)
            => Task.FromResult(1L);

        public Task<long> LogSectionActionAsync(long templateId, string templateName, TemplateAction action, long sectionId, string sectionName, object? changeDetails = null, CancellationToken ct = default)
            => Task.FromResult(1L);

        public Task<long> LogFieldActionAsync(long templateId, string templateName, TemplateAction action, long fieldId, string fieldName, object? changeDetails, CancellationToken ct)
            => Task.FromResult(1L);
    }

    sealed class StubUserService : IUserService
    {
        public Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
            => Task.FromResult(new UserResponse(
                Id: 1,
                PublicId: 2,
                ClientReferenceId: request.ClientReferenceId ?? 3,
                FirstName: request.FirstName,
                LastName: request.LastName,
                Email: request.Email,
                Phone: request.Phone,
                RoleId: request.RoleId,
                RoleName: "User",
                DeptId: request.DeptId
            ));

        public Task<UserResponse?> GetByIdAsync(long id, CancellationToken ct = default)
            => Task.FromResult<UserResponse?>(new UserResponse(
                Id: id,
                PublicId: 22,
                ClientReferenceId: 33,
                FirstName: "A",
                LastName: "B",
                Email: "a@b.com",
                Phone: null,
                RoleId: 1,
                RoleName: "User",
                DeptId: null
            ));

        // Interface now expects BOTH overloads – implement each.
        public Task<IReadOnlyList<UserResponse>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UserResponse>>(new List<UserResponse>());

        public Task<IReadOnlyList<UserResponse>> GetAllAsync(bool includeInactive, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UserResponse>>(new List<UserResponse>());

        public Task<UserResponse?> UpdateAsync(long id, UpdateUserRequest request, CancellationToken ct = default)
            => Task.FromResult<UserResponse?>(new UserResponse(
                Id: id,
                PublicId: 22,
                ClientReferenceId: request.ClientReferenceId ?? 33,
                FirstName: request.FirstName ?? "A",
                LastName: request.LastName ?? "B",
                Email: "a@b.com",
                Phone: request.Phone,
                RoleId: request.RoleId ?? 1,
                RoleName: "User",
                DeptId: request.DeptId
            ));

        public Task<bool> DeleteAsync(long id, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<UserResponse?> GetByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult<UserResponse?>(null);

        // Required by interface
        public Task UpdateRoleAsync(UpdateUserRoleRequest req, long performedBy, CancellationToken ct = default)
            => Task.CompletedTask;

        // Required by interface
        public Task<PagedResult<UserListItemResponse>> GetUsersAsync(UserListQuery query, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<UserListItemResponse>
            {
                Page = query.Page <= 0 ? 1 : query.Page,
                PageSize = query.PageSize <= 0 ? 20 : query.PageSize,
                TotalCount = 0,
                Items = Array.Empty<UserListItemResponse>()
            });

        // Safe to leave these extras (no harm if not in interface; they just won't be called)
        public Task<bool> DeprovisionAsync(long id, string reason, long adminId, bool hardDelete, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> ReprovisionAsync(long id, CancellationToken ct = default)
            => Task.FromResult(true);
    }


    sealed class StubTemplateVersionService : ITemplateVersionService
    {
        public Task<TemplateVersionResponse> CreateAsync(CreateTemplateVersionRequest request, CancellationToken ct, TemplateMode mode = TemplateMode.Default)
            => Task.FromResult(Sample(10, 1));

        public Task<TemplateVersionResponse?> GetByIdAsync(long versionId, CancellationToken ct)
            => Task.FromResult<TemplateVersionResponse?>(Sample(versionId, 1));

        public Task<IReadOnlyList<TemplateVersionResponse>> ListByTemplateAsync(long templateId, int page = 1, int pageSize = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TemplateVersionResponse>>(new[] { Sample(10, 1) });

        public Task<TemplateVersionResponse> UpdateAsync(long versionId, UpdateTemplateVersionRequest request, CancellationToken ct)
            => Task.FromResult(Sample(versionId, 1));

        public Task<TemplateVersionResponse> ActivateAsync(long versionId, CancellationToken ct)
            => Task.FromResult(Sample(versionId, 1));

        public Task<TemplateVersionResponse> DeactivateAsync(long versionId, CancellationToken ct)
            => Task.FromResult(Sample(versionId, 1));

        public Task SoftDeleteAsync(long versionId, string rowVersionBase64, CancellationToken ct) => Task.CompletedTask;

        public Task<TemplateVersionResponse> PatchAsync(long versionId, UpdateTemplateVersionRequest request, CancellationToken ct)
            => Task.FromResult(Sample(versionId, 1));

        public Task EnsureActivatedIfInvitedAsync(long versionId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<VersionForkResult> ForkWithMappingAsync(long sourceVersionId, CancellationToken ct)
            => Task.FromResult(new VersionForkResult
            {
                SourceVersionId = sourceVersionId,
                TargetVersionId = sourceVersionId + 1000,
                SectionIdMap = new Dictionary<long, long>()
            });

        private static TemplateVersionResponse Sample(long id, int num) => new TemplateVersionResponse
        {
            VersionId = id,
            TemplateId = 7,
            VersionNumber = num,
            IsActive = true,
            Status = "Draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = 1,
            CreatedByName = "Tester",
            CreatedByEmail = "tester@example.com",
            RowVersionBase64 = "dGVzdA=="
        };
    }

    sealed class StubSectionFieldMappingService : ISectionFieldMappingService
    {
        private static JsonElement EmptyObject()
            => JsonDocument.Parse("{}").RootElement.Clone();

        public Task<SectionFieldMappingDto> CreateAsync(CreateSectionFieldMappingDto dto, CancellationToken ct = default)
            => Task.FromResult(new SectionFieldMappingDto
            {
                Id = 1,
                TemplateSectionId = dto.TemplateSectionId,
                Structure = EmptyObject(),
                CaptureAllowed = true,
                UploadAllowed = true
            });

        public Task<bool> DeleteAsync(long id, CancellationToken ct = default) => Task.FromResult(true);

        public Task<SectionFieldMappingDto?> GetByIdAsync(long id, CancellationToken ct = default)
            => Task.FromResult<SectionFieldMappingDto?>(new SectionFieldMappingDto
            {
                Id = id,
                TemplateSectionId = 99,
                Structure = EmptyObject(),
                CaptureAllowed = true,
                UploadAllowed = true
            });

        public Task<IEnumerable<SectionFieldMappingDto>> GetBySectionIdAsync(long sectionId, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<SectionFieldMappingDto>>(new[]
            {
            new SectionFieldMappingDto
            {
                Id = 1,
                TemplateSectionId = sectionId,
                Structure = EmptyObject(),
                CaptureAllowed = true,
                UploadAllowed = true
            }
            });

        public Task<SectionFieldMappingDto?> PatchBySectionIdAsync(long sectionId, UpdateSectionFieldMappingDto dto, CancellationToken ct = default)
            => Task.FromResult<SectionFieldMappingDto?>(new SectionFieldMappingDto
            {
                Id = 1,
                TemplateSectionId = sectionId,
                Structure = dto.Structure ?? EmptyObject(),
                CaptureAllowed = dto.CaptureAllowed ?? true,
                UploadAllowed = dto.UploadAllowed ?? true
            });

        public Task<SectionFieldMappingDto?> UpdateAsync(long id, UpdateSectionFieldMappingDto dto, CancellationToken ct = default)
            => Task.FromResult<SectionFieldMappingDto?>(new SectionFieldMappingDto
            {
                Id = id,
                TemplateSectionId = 99,
                Structure = dto.Structure ?? EmptyObject(),
                CaptureAllowed = dto.CaptureAllowed ?? true,
                UploadAllowed = dto.UploadAllowed ?? true
            });
    }


    sealed class StubFilesService : IFilesService
    {
        public Task<FileResponse> CreateAsync(CreateFileRequest req, CancellationToken ct = default)
            => Task.FromResult(new FileResponse { Id = 1, FileName = req.FileName, ContentType = req.ContentType });

        public Task<FileUploadResponse> CreateWithMappingAsync(CreateFileRequest req, long userTemplateSubmissionId, CancellationToken ct = default)
            => Task.FromResult(default(FileUploadResponse)!); // not exercised by these tests

        public Task<PagedResult<FileResponse>> SearchAsync(Guid? documentDefinitionId, FileStatus? status, string? q, int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<FileResponse>
            {
                Items = Array.Empty<FileResponse>(),
                Page = page,
                PageSize = pageSize,
                TotalCount = 0
            });

        public Task<List<FileResponse>> GetFilesBySubmissionIdAsync(long submissionId, CancellationToken ct = default)
            => Task.FromResult(new List<FileResponse>());

        public Task<FileResponse?> GetByIdAsync(long id, CancellationToken ct = default)
            => Task.FromResult<FileResponse?>(new FileResponse { Id = id, FileName = "x", ContentType = "application/pdf" });

        public Task<List<long>> GetSubmissionIdsByFileIdAsync(long id, CancellationToken ct = default)
            => Task.FromResult(new List<long>());

        public Task<FileResponse> UpdateAsync(long id, UpdateFileRequest request, CancellationToken ct = default)
            => Task.FromResult(new FileResponse { Id = id, FileName = request.FileName ?? "x", ContentType = "application/pdf" });

        public Task<bool> SoftDeleteAsync(long id, CancellationToken ct = default) => Task.FromResult(true);
    }

    sealed class StubStorage : IFileStorage
    {
        public Task<FileStorageResult> SaveAsync(Microsoft.AspNetCore.Http.IFormFile file, string bucket, string? objectKey = null, CancellationToken ct = default)
            => Task.FromResult(new FileStorageResult
            {
                Bucket = bucket,
                ObjectKey = objectKey ?? "obj",
                Length = file.Length,
                ContentType = file.ContentType,
                ChecksumSha256 = null,
                LocalPath = null
            });

        public Task<Stream> OpenReadAsync(string bucket, string objectKey, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("encrypted bytes")));

        public Task<bool> DeleteAsync(string bucket, string objectKey, CancellationToken ct = default)
            => Task.FromResult(true);

        public string? GetPublicUrl(string bucket, string objectKey)
            => $"https://local/{bucket}/{objectKey}";
    }

    sealed class StubEncryption : IEncryptionService
    {
        public Task<string> GetMasterKeyIdAsync(CancellationToken ct = default) => Task.FromResult("k1");
        public Task<string> GenerateKeyAsync(string? purpose = null, CancellationToken ct = default) => Task.FromResult("k1");

        public Task<EncryptionResult> EncryptStreamAsync(Stream inputStream, Stream outputStream, EncryptionParams? parameters = null, CancellationToken ct = default)
        {
            inputStream.CopyTo(outputStream);
            return Task.FromResult(new EncryptionResult
            {
                EncryptedData = Array.Empty<byte>(),
                IV = Array.Empty<byte>(),
                Tag = Array.Empty<byte>(),
                KeyId = "k1",
                Algorithm = "AES-256-CBC",
                EncryptedAt = DateTime.UtcNow,
                PlaintextLength = 0
            });
        }

        public Task<EncryptionResult> EncryptAsync(byte[] plaintext, EncryptionParams? parameters = null, CancellationToken ct = default)
        {
            return Task.FromResult(new EncryptionResult
            {
                EncryptedData = plaintext,
                IV = Array.Empty<byte>(),
                Tag = Array.Empty<byte>(),
                KeyId = "k1",
                Algorithm = "AES-256-GCM",
                EncryptedAt = DateTime.UtcNow,
                PlaintextLength = plaintext.Length
            });
        }

        public Task<long> DecryptStreamAsync(Stream inputStream, Stream outputStream, DecryptionParams parameters, CancellationToken ct = default)
        {
            inputStream.CopyTo(outputStream);
            return Task.FromResult(0L);
        }

        public Task<byte[]> DecryptAsync(byte[] ciphertext, DecryptionParams parameters, CancellationToken ct = default)
            => Task.FromResult(ciphertext);
    }

    sealed class StubNotifier : IHandoffNotifier
    {
        public Task FileUploadCompleted(long submissionId, object payload) => Task.CompletedTask;
        public Task SectionProgressUpdated(long submissionId, object payload) => Task.CompletedTask;
        public Task PersonalUpdated(long submissionId, object payload) => Task.CompletedTask;
    }

    sealed class StubTemplatesLinkService : ITemplatesLinkGenerationService
    {
        public Task<LinkResponse> CreateAsync(CreateLinkRequest request, CancellationToken ct = default)
            => Task.FromResult(new LinkResponse(
                LinkId: 1,
                UserId: request.UserId,
                ShortCode: "abc",
                TemplateVersionId: request.TemplateVersionId,
                User: null,
                ExpiresAtUtc: request.ExpiresAtUtc,
                EmailTemplateId: request.EmailTemplateId));

        [Obsolete("Use CreateAsync(CreateLinkRequest, ct). Expiry must be provided by the frontend.")]
        public Task<LinkResponse> CreateAsync(long userId, long templateVersionId, CancellationToken ct = default)
            => Task.FromResult(new LinkResponse(
                LinkId: 2,
                UserId: userId,
                ShortCode: "abc",
                TemplateVersionId: templateVersionId,
                User: null,
                ExpiresAtUtc: DateTime.UtcNow.AddDays(1),
                EmailTemplateId: null));

        public Task<LinkResponse?> ResolveAsync(string shortCode, CancellationToken ct = default)
            => Task.FromResult<LinkResponse?>(new LinkResponse(
                LinkId: 3,
                UserId: 0,
                ShortCode: shortCode,
                TemplateVersionId: 9,
                User: null,
                ExpiresAtUtc: DateTime.UtcNow.AddDays(1),
                EmailTemplateId: null));

        public Task<int> CleanupExpiredLinksAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    #endregion

    [TestFixture]
    public class AuthorizationSpecs
    {
        private ControllerOnlyFactory _factory = null!;

        [SetUp]
        public void Setup() => _factory = new ControllerOnlyFactory();

        #region UsersController (Perm:ManageUsersAndRoles)

        [Test]
        public async Task Users_GetAll_requires_perm_ManageUsersAndRoles()
        {
            var noPerms = _factory.CreateClientWith(authenticated: true);
            var res1 = await noPerms.GetAsync("/api/Users");
            res1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var withPerm = _factory.CreateClientWith(perms: new[] { "ManageUsersAndRoles" });
            var res2 = await withPerm.GetAsync("/api/Users");
            res2.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Test]
        public async Task Users_GetById_requires_perm_ManageUsersAndRoles()
        {
            var noPerms = _factory.CreateClientWith(authenticated: true);
            var res1 = await noPerms.GetAsync("/api/Users/1");
            res1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var withPerm = _factory.CreateClientWith(perms: new[] { "ManageUsersAndRoles" });
            var res2 = await withPerm.GetAsync("/api/Users/1");
            res2.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        #endregion

        #region RolesController (Perm:ConfigureRbac for key endpoints; Admin for create/update/delete)

        [Test]
        public async Task Roles_PermissionMatrix_requires_perm_ConfigureRbac()
        {
            var noPerms = _factory.CreateClientWith(authenticated: true);
            var res1 = await noPerms.GetAsync("/api/Roles/permission-matrix");
            res1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var withPerm = _factory.CreateClientWith(perms: new[] { "ConfigureRbac" });
            var res2 = await withPerm.GetAsync("/api/Roles/permission-matrix");
            res2.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Test]
        public async Task Roles_Create_requires_Admin_role()
        {
            var nonAdmin = _factory.CreateClientWith(perms: new[] { "ConfigureRbac" });
            var res1 = await nonAdmin.PostAsync("/api/Roles", new StringContent("{}", Encoding.UTF8, "application/json"));
            res1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var admin = _factory.CreateClientWith(role: "Admin");
            var res2 = await admin.PostAsync("/api/Roles", new StringContent("{\"name\":\"A\"}", Encoding.UTF8, "application/json"));
            res2.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        }

        #endregion

        #region TemplateVersionController ([Authorize(Policy = RequireAdmin)] for writes, AllowAnonymous for reads)

        [Test]
        public async Task TemplateVersion_GetById_is_allow_anonymous()
        {
            var anon = _factory.CreateClient(); // no auth headers
            var res = await anon.GetAsync("/api/TemplateVersion/123");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Test]
        public async Task TemplateVersion_Create_requires_Admin()
        {
            var nonAdmin = _factory.CreateClientWith(authenticated: true);
            var body = JsonContent(new CreateTemplateVersionRequest { TemplateId = 1 });
            var res1 = await nonAdmin.PostAsync("/api/TemplateVersion", body);
            res1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var admin = _factory.CreateClientWith(role: "Admin");
            var res2 = await admin.PostAsync("/api/TemplateVersion", body);
            res2.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        #endregion

        #region TemplateSectionController ([Authorize(Policy = "Perm:CreateEditWorkflows")])

        [Test]
        public async Task TemplateSection_List_requires_CreateEditWorkflows_permission()
        {
            var noPerms = _factory.CreateClientWith(authenticated: true);
            var res1 = await noPerms.GetAsync("/api/TemplateSection/versions/5/sections");
            res1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // Controller requires Perm:CreateEditWorkflows
            var withPerm = _factory.CreateClientWith(perms: new[] { "CreateEditWorkflows" });
            var res2 = await withPerm.GetAsync("/api/TemplateSection/versions/5/sections");
            res2.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        }

        #endregion

        #region SectionFieldMappingController ([Authorize(Policy = "Perm:CreateEditWorkflows")])

        [Test]
        public async Task SectionFieldMapping_requires_CreateEditWorkflows_permission_for_reads()
        {
            var anon = _factory.CreateClient();
            var res1 = await anon.GetAsync("/api/section-field-mappings/1");
            res1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            // Controller is protected by Perm:CreateEditWorkflows even for GET
            var authed = _factory.CreateClientWith(
                authenticated: true,
                perms: new[] { "CreateEditWorkflows", "ManageUsersAndRoles", "ConfigureRbac" }
            );
            var res2 = await authed.GetAsync("/api/section-field-mappings/1");
            res2.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        #endregion

        #region TemplatesLinkGenerationsController ([Authorize(Policy = RequireAdmin)] for POST, AllowAnonymous resolve)

        [Test]
        public async Task LinkGen_Create_requires_Admin()
        {
            var nonAdmin = _factory.CreateClientWith(authenticated: true);
            var body = JsonContent(new CreateLinkRequest(1, 9, DateTime.UtcNow.AddDays(1), null));
            var r1 = await nonAdmin.PostAsync("/api/templates-link-generation", body);
            r1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var admin = _factory.CreateClientWith(role: "Admin");
            var r2 = await admin.PostAsync("/api/templates-link-generation", body);
            r2.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        [Test]
        public async Task LinkGen_Resolve_is_allow_anonymous()
        {
            var anon = _factory.CreateClient();
            var r = await anon.GetAsync("/api/templates-link-generation/resolve?shortCode=abc");
            r.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        #endregion

        #region FilesController (class is [AllowAnonymous] in your code)

        [Test]
        public async Task Files_Search_is_allow_anonymous()
        {
            var anon = _factory.CreateClient();
            var r = await anon.GetAsync("/api/Files");
            r.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        #endregion

        private static StringContent JsonContent<T>(T obj)
            => new StringContent(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");
    }
}
