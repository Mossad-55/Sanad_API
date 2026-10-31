using MimeKit;
using Ahlena.Modules.Identity.Infrastructure.Messaging;

namespace Ahlena.UnitTests.Identity.Infrastructure;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public void CreateBody_UsesMultipartAlternativeWithPlainAndHtmlTwins()
    {
        MimeEntity body = SmtpEmailSender.CreateBody("Plain copy", "<p>HTML copy</p>");

        Multipart alternative = Assert.IsType<Multipart>(body);
        Assert.Equal("alternative", alternative.ContentType.MediaSubtype);
        Assert.Equal("Plain copy", Assert.IsType<TextPart>(alternative[0]).Text);
        Assert.Equal("<p>HTML copy</p>", Assert.IsType<TextPart>(alternative[1]).Text);
        Assert.Equal("plain", Assert.IsType<TextPart>(alternative[0]).ContentType.MediaSubtype);
        Assert.Equal("html", Assert.IsType<TextPart>(alternative[1]).ContentType.MediaSubtype);
    }

    [Fact]
    public void CreateBody_UsesPlainTextForLegacyMessagesWithoutHtml()
    {
        MimeEntity body = SmtpEmailSender.CreateBody("Plain copy", null);

        TextPart plain = Assert.IsType<TextPart>(body);
        Assert.Equal("plain", plain.ContentType.MediaSubtype);
        Assert.Equal("Plain copy", plain.Text);
    }
}
