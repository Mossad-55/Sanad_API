using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Security.Cryptography;
using System.Text;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Authentication;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Support;
using Sanad.Modules.Identity.Infrastructure.Messaging.EmailTemplates;

namespace Sanad.Modules.Identity.Infrastructure.Messaging;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(
        IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public Task SendEmailAsync(string email, string subject, string body, string? correlationId, CancellationToken cancellationToken) =>
        SendEmailAsync(email, subject, body, null, correlationId, cancellationToken);

    public async Task SendEmailAsync(string email, string subject, string plainTextBody, string? htmlBody, string? correlationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(_options.FromName) ? "Sanad Care" : _options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        string? messageId = CreateMessageId(correlationId);
        if (messageId is not null) message.MessageId = messageId;
        message.Body = CreateBody(plainTextBody, htmlBody);

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, _options.UseSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrWhiteSpace(_options.Username)) await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static string? CreateMessageId(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId)) return null;
        string stableId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(correlationId)));
        return $"<sanad-{stableId}@sanad-care>";
    }

    internal static MimeEntity CreateBody(string plainTextBody, string? htmlBody)
    {
        if (string.IsNullOrWhiteSpace(htmlBody))
            return new TextPart("plain") { Text = plainTextBody };

        return new Multipart("alternative")
        {
            new TextPart("plain") { Text = plainTextBody },
            new TextPart("html") { Text = htmlBody }
        };
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
        string arabicPurpose = purpose switch
        {
            VerificationPurpose.ResetPassword => "إعادة تعيين كلمة المرور",
            VerificationPurpose.VerifyEmail => "تأكيد البريد الإلكتروني",
            _ => "التحقق"
        };
        string englishPurpose = purpose switch
        {
            VerificationPurpose.ResetPassword => "password reset",
            VerificationPurpose.VerifyEmail => "email verification",
            _ => "verification"
        };
        var model = EmailTemplateModel.ForOtpCode(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["code"] = code,
                ["expiresIn"] = $"{(int)OtpPolicy.Lifetime.TotalMinutes} minutes"
            }, arabicPurpose, englishPurpose);
        RenderedEmailTemplate rendered = EmailTemplateCatalog.Render(EmailTemplateKind.OtpCode, model);
        var message = new MimeMessage();

        message.From.Add(
            new MailboxAddress(
                string.IsNullOrWhiteSpace(_options.FromName)
                    ? "Sanad Care"
                    : _options.FromName,
                _options.FromAddress));

        message.To.Add(
            MailboxAddress.Parse(email));

        message.Subject = rendered.Subject;
        message.Body = CreateBody(rendered.PlainTextBody, rendered.HtmlBody);

        return message;
    }

    public async Task SendFamilyInvitationAsync(
    string email,
    string familyName,
    string inviteLink,
    CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MimeMessage message =
            CreateInvitationMessage(
                email,
                familyName,
                inviteLink);

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

    private MimeMessage CreateInvitationMessage(
        string email,
        string familyName,
        string inviteLink)
    {
        RenderedEmailTemplate rendered = EmailTemplateCatalog.Render(
            EmailTemplateKind.Invitation,
            new EmailTemplateModel(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["familyName"] = familyName,
                ["inviteLink"] = inviteLink
            }));
        var message = new MimeMessage();

        message.From.Add(
            new MailboxAddress(
                string.IsNullOrWhiteSpace(_options.FromName)
                    ? "Sanad Care"
                    : _options.FromName,
                _options.FromAddress));

        message.To.Add(
            MailboxAddress.Parse(email));

        message.Subject = rendered.Subject;
        message.Body = CreateBody(rendered.PlainTextBody, rendered.HtmlBody);

        return message;
    }

    public async Task SendSupportRequestAsync(
        string senderName,
        string? senderEmail,
        string senderPhoneNumber,
        string accountTypes,
        string subject,
        string message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mimeMessage = new MimeMessage();

        mimeMessage.From.Add(
            new MailboxAddress(
                string.IsNullOrWhiteSpace(_options.FromName)
                    ? "Sanad Care"
                    : _options.FromName,
                _options.FromAddress));

        mimeMessage.To.Add(
            MailboxAddress.Parse(
                string.IsNullOrWhiteSpace(_options.SupportInboxAddress)
                    ? _options.FromAddress
                    : _options.SupportInboxAddress));

        if (!string.IsNullOrWhiteSpace(senderEmail))
        {
            mimeMessage.ReplyTo.Add(
                MailboxAddress.Parse(senderEmail));
        }

        string bodyAr =
            $"المرسل: {senderName}\n" +
            $"البريد الإلكتروني: {senderEmail ?? "—"}\n" +
            $"رقم الهاتف: {senderPhoneNumber}\n" +
            $"نوع الحساب: {accountTypes}\n" +
            $"الموضوع: {subject}\n" +
            $"الرسالة: {message}";
        string bodyEn =
            $"Sender: {senderName}\n" +
            $"Email: {senderEmail ?? "—"}\n" +
            $"Phone: {senderPhoneNumber}\n" +
            $"Account types: {accountTypes}\n" +
            $"Subject: {subject}\n" +
            $"Message: {message}";
        RenderedEmailTemplate rendered = EmailTemplateCatalog.RenderBilingual(
            "طلب دعم جديد من سند", "New Sanad Care support request",
            "طلب دعم جديد", "New support request", bodyAr, bodyEn);
        mimeMessage.Subject = rendered.Subject;
        mimeMessage.Body = CreateBody(rendered.PlainTextBody, rendered.HtmlBody);

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
            mimeMessage,
            cancellationToken);

        await client.DisconnectAsync(
            true,
            cancellationToken);
    }

    public async Task SendSupportReplyAsync(
        string? recipientEmail,
        string subject,
        string replyBody,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(
            string.IsNullOrWhiteSpace(_options.FromName) ? "Sanad Care" : _options.FromName,
            _options.FromAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(recipientEmail));
        mimeMessage.Subject = $"Re: {subject}";
        mimeMessage.Body = new TextPart("plain") { Text = replyBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, _options.UseSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        }
        await client.SendAsync(mimeMessage, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
