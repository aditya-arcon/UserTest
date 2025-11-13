using IDV_Backend.Services.Email;
using IDV_Backend.Services.Email.Templates;
using Moq;
using NUnit.Framework;

// Add an explicit using alias for RenderedEmail to resolve ambiguity
using RenderedEmail = IDV_Backend.Services.Email.Templates.RenderedEmail;

namespace UserTest.Services.Email
{
    public sealed class EmailServiceTests
    {
        private readonly Mock<IEmailTemplateRenderer> _renderer = new();
        private readonly Mock<IEmailSender> _sender = new();
        private readonly Mock<Microsoft.Extensions.Logging.ILogger<EmailService>> _logger = new();

        private EmailService CreateSut() => new(_renderer.Object, _sender.Object, _logger.Object);

        [Test]
        public async Task SendInviteAsync_RendersAndSends_WithHtml()
        {
            // arrange
            var ctx = new InviteEmailContext(
                FirstName: "Alice",
                VerificationLink: "https://x/verify/abc",
                ExpiryAtUtc: DateTime.UtcNow.AddDays(1),
                CompanyName: "ACME",
                SupportContact: "help@acme.test",
                Department: "Ops",
                Role: "User");

            _renderer.Setup(r => r.RenderInviteAsync(10, ctx, null, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new RenderedEmail("Hi Alice", "<b>Click</b>", true));

            // act
            var sut = CreateSut();
            await sut.SendInviteAsync("to@example.com", ctx, templateVersionId: 10, emailTemplateId: null, ct: default);

            // assert
            _sender.Verify(s => s.SendAsync("to@example.com", "Hi Alice", "<b>Click</b>", true, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void SendInviteAsync_Throws_WhenRecipientMissing()
        {
            var sut = CreateSut();
            var ctx = new InviteEmailContext("Bob", "link", DateTime.UtcNow, "ACME", "support", null, null);
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await sut.SendInviteAsync("", ctx, null, null, default));
        }

        [Test]
        public async Task SendInviteAsync_PlainText_Render()
        {
            var ctx = new InviteEmailContext("Eve", "link", null, "ACME", "support", null, null);

            _renderer.Setup(r => r.RenderInviteAsync(null, ctx, 777, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new RenderedEmail("Subject", "Body", false));

            var sut = CreateSut();
            await sut.SendInviteAsync("eve@example.com", ctx, null, 777, default);

            _sender.Verify(s => s.SendAsync("eve@example.com", "Subject", "Body", false, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
