using System.Linq;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Contracts.Roles.Validators;
using NUnit.Framework;

namespace UserTest.Contracts.Roles
{
    [TestFixture]
    public class UpdateRoleRequestValidatorTests
    {
        private UpdateRoleRequestValidator _sut = null!;

        [SetUp]
        public void SetUp() => _sut = new UpdateRoleRequestValidator();

        [Test]
        public void ValidInput_Passes()
        {
            var req = new UpdateRoleRequest("WorkflowAdmin");

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.True, string.Join(" | ", res.Errors.Select(e => e.ErrorMessage)));
        }

        [Test]
        public void EmptyName_Fails()
        {
            var req = new UpdateRoleRequest("");

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e => e.PropertyName == nameof(UpdateRoleRequest.Name)), Is.True);
        }

        [Test]
        public void InvalidName_Fails()
        {
            var req = new UpdateRoleRequest("NotARole");

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e => e.PropertyName == nameof(UpdateRoleRequest.Name)), Is.True);
        }

        [Test]
        public void TooLongName_Fails()
        {
            var name = new string('X', 51);
            var req = new UpdateRoleRequest(name);

            var res = _sut.Validate(req);

            Assert.That(res.IsValid, Is.False);
            Assert.That(res.Errors.Any(e => e.PropertyName == nameof(UpdateRoleRequest.Name)), Is.True);
        }
    }
}
