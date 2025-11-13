using FluentValidation.TestHelper;
using IDV_Backend.Contracts.Roles;
using IDV_Backend.Contracts.Roles.Validators;
using IDV_Backend.Models.Roles;
using NUnit.Framework;

namespace UserTest.RBAC;

[TestFixture]
public class UpdateRolePermissionsRequestValidatorTests
{
    private UpdateRolePermissionsRequestValidator _validator = null!;

    [SetUp]
    public void SetUp() => _validator = new UpdateRolePermissionsRequestValidator();

    [Test]
    public void EmptyCodes_ShouldFail()
    {
        var req = new UpdateRolePermissionsRequest(Array.Empty<string>());
        var res = _validator.TestValidate(req);
        res.ShouldHaveValidationErrorFor(x => x.Codes);
    }

    [Test]
    public void DuplicateCodes_ShouldFail()
    {
        var c = PermissionCodes.ManageUsersAndRoles;
        var req = new UpdateRolePermissionsRequest(new[] { c, c });
        var res = _validator.TestValidate(req);
        res.ShouldHaveValidationErrorFor(x => x.Codes);
    }

    [Test]
    public void UnknownCode_ShouldFail()
    {
        var req = new UpdateRolePermissionsRequest(new[] { "Nope" });
        var res = _validator.TestValidate(req);
        res.ShouldHaveValidationErrorFor("Codes[0]");
    }

    [Test]
    public void ValidCodes_ShouldPass()
    {
        var req = new UpdateRolePermissionsRequest(new[]
        {
            PermissionCodes.ManageUsersAndRoles,
            PermissionCodes.ViewRespondVerifs
        });

        var res = _validator.TestValidate(req);
        res.ShouldNotHaveAnyValidationErrors();
    }
}
