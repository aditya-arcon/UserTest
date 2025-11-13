using System.Linq;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Contracts.Roles.Validators;
using IDV_Backend.Models.Roles;
using NUnit.Framework;

namespace UserTest.Contracts.Roles
{
    [TestFixture]
    public class UpdateRolePermissionsRequestValidatorTests
    {
        private UpdateRolePermissionsRequestValidator _sut = null!;

        [SetUp]
        public void SetUp() => _sut = new UpdateRolePermissionsRequestValidator();

        [Test]
        public void ValidInput_Passes()
        {
            var req = new UpdateRolePermissionsRequest(new[]
            {
                PermissionCodes.ManageUsersAndRoles,
                PermissionCodes.ConfigureRbac
            });

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.True, string.Join(" | ", res.Errors.Select(e => e.ErrorMessage)));
        }

        [Test]
        public void NullCodes_Fails()
        {
            var req = new UpdateRolePermissionsRequest(null!);

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e => e.PropertyName == nameof(UpdateRolePermissionsRequest.Codes)), Is.True);
        }

        [Test]
        public void EmptyCodes_Fails()
        {
            var req = new UpdateRolePermissionsRequest(System.Array.Empty<string>());

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e => e.PropertyName == nameof(UpdateRolePermissionsRequest.Codes)), Is.True);
        }

        [Test]
        public void DuplicateCodes_Fails()
        {
            var c = PermissionCodes.ViewRespondVerifs;
            var req = new UpdateRolePermissionsRequest(new[] { c, c });

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e => e.PropertyName == nameof(UpdateRolePermissionsRequest.Codes)), Is.True);
        }

        [Test]
        public void EmptyCodeEntry_Fails()
        {
            var req = new UpdateRolePermissionsRequest(new[] { "" });

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e => e.PropertyName == "Codes[0]"), Is.True);
        }

        [Test]
        public void UnknownCode_Fails()
        {
            var req = new UpdateRolePermissionsRequest(new[] { "NotARealCode" });

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e =>
                e.PropertyName == "Codes[0]" &&
                e.ErrorMessage.StartsWith("Unknown permission code")), Is.True);
        }
    }
}
