using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Authentication;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;

namespace Sanad.Modules.Identity.Infrastructure.Messaging;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(
        IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendVerificationCodeAsync(
        string email,
        string code,
        VerificationPurpose purpose,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MimeMessage message = CreateMessage(
            email,
            code,
            purpose);

        using var client = new SmtpClient();

        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.UseSsl
                ? SecureSocketOptions.Auto
                : SecureSocketOptions.None,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            await client.AuthenticateAsync(
                _options.Username,
                _options.Password,
                cancellationToken);
        }

        await client.SendAsync(
            message,
            cancellationToken);

        await client.DisconnectAsync(
            true,
            cancellationToken);
    }

    private MimeMessage CreateMessage(
        string email,
        string code,
        VerificationPurpose purpose)
    {
        var message = new MimeMessage();

        message.From.Add(
            new MailboxAddress(
                string.IsNullOrWhiteSpace(_options.FromName)
                    ? "Sanad Care"
                    : _options.FromName,
                _options.FromAddress));

        message.To.Add(
            MailboxAddress.Parse(email));

        message.Subject =
            "Sanad Care verification code | رمز التحقق من سند";

        message.Body = new TextPart("plain")
        {
            Text = CreateBody(code, purpose)
        };

        return message;
    }

    private static string CreateBody(
        string code,
        VerificationPurpose purpose)
    {
        string arabicPurpose = purpose switch
        {
            VerificationPurpose.ResetPassword =>
                "إعادة تعيين كلمة المرور",
            VerificationPurpose.VerifyEmail =>
                "تأكيد البريد الإلكتروني",
            _ =>
                "التحقق"
        };

        string englishPurpose = purpose switch
        {
            VerificationPurpose.ResetPassword =>
                "password reset",
            VerificationPurpose.VerifyEmail =>
                "email verification",
            _ =>
                "verification"
        };

        int lifetimeMinutes = (int)OtpPolicy.Lifetime.TotalMinutes;

        return
            $"رمز {arabicPurpose} الخاص بك في سند هو: {code}{Environment.NewLine}" +
            $"صالح لمدة {lifetimeMinutes} دقائق.{Environment.NewLine}" +
            Environment.NewLine +
            $"Your Sanad Care {englishPurpose} code is: {code}{Environment.NewLine}" +
            $"It expires in {lifetimeMinutes} minutes.";
    }
}