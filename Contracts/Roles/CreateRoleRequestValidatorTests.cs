using System.Linq;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Contracts.Roles.Validators;
using IDV_Backend.Models.Roles;
using NUnit.Framework;

namespace UserTest.Contracts.Roles
{
    [TestFixture]
    public class CreateRoleRequestValidatorTests
    {
        private CreateRoleRequestValidator _sut = null!;

        [SetUp]
        public void SetUp() => _sut = new CreateRoleRequestValidator();

        [Test]
        public void ValidInput_Passes()
        {
            var req = new CreateRoleRequest(
                "SupportAdmin",
                new[] { PermissionCodes.ManageSupportTickets, PermissionCodes.ViewRespondVerifs });

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.True, string.Join(" | ", res.Errors.Select(e => e.ErrorMessage)));
        }

        [Test]
        public void EmptyName_Fails()
        {
            var req = new CreateRoleRequest(
                "",
                new[] { PermissionCodes.ManageSupportTickets });

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e => e.PropertyName == nameof(CreateRoleRequest.Name)), Is.True);
        }

        [Test]
        public void InvalidName_Fails()
        {
            var req = new CreateRoleRequest(
                "DefinitelyNotARole",
                new[] { PermissionCodes.ManageSupportTickets });

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e => e.PropertyName == nameof(CreateRoleRequest.Name)), Is.True);
        }

        [Test]
        public void NullCodes_Fails()
        {
            var req = new CreateRoleRequest("Admin", null!);

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e => e.PropertyName == nameof(CreateRoleRequest.Codes)), Is.True);
        }

        [Test]
        public void EmptyCodes_Fails()
        {
            var req = new CreateRoleRequest("Admin", System.Array.Empty<string>());

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e => e.PropertyName == nameof(CreateRoleRequest.Codes)), Is.True);
        }

        [Test]
        public void DuplicateCodes_Fails()
        {
            var dup = PermissionCodes.ViewRespondVerifs;
            var req = new CreateRoleRequest("Admin", new[] { dup, dup });

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e => e.PropertyName == nameof(CreateRoleRequest.Codes)), Is.True);
        }

        [Test]
        public void EmptyCodeEntry_Fails()
        {
            var req = new CreateRoleRequest("Admin", new[] { "" });

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            // This error comes from RuleForEach(Codes)
            Assert.That(res.Errors.Any(e => e.PropertyName == "Codes[0]"), Is.True);
        }
    }
}
