using System.Security.Cryptography;
using System.Text;
using Sanad.Modules.Identity.Infrastructure.Messaging.EmailTemplates;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class EmailTemplateTests
{
    private static readonly string[] ExpectedSnapshots =
    [
        "2ABA136226DC79E4B8C48D9C02F8B59D2EAF87E9C7A59E9498C0C867CD8137ED",
        "54A396A2957D0FC8E2DB8FF0D7EF2D1DD0021711C89238D698605FAADA493AC9",
        "08A8EF7CD6FCC0C210BE9BA317725D62EA6FC262BF33D2F5AF3F836FD7874F49",
        "6D09B21E0CCA3999C0809A6D9A2E99A3CDBDA6C377A34CB8D3D34BD5DB544F06",
        "CCDB6EC90BCDC5DB00F2993FEC21DB15D2445BF94BCEC4E02C52806EB3930B57",
        "D4AA2F9809BD2134E39F5FD6E834AE36B374C626DA23E23274FDAE2D26E3AE35",
        "59C7E8CE3E03DD7F9111B4D31359A3FD13EEDE00CD71E98DFF49EFD363990862",
        "B582363580A1C37D1D8940C81736EF9929833D5927EB731E3C62CCF88884E7C3",
        "AE890DBDBF57D1625E9E9ACB503F1AC56EB829ED9F88689701604FB4AD9529F3",
        "C8672B5E893E9DE335135AE6DCC5D7B8A8B6CECB2B830823735E9F293322EC9E",
        "5B79B104DF0C1AED8FB1ADF3CFFF7D3CE21658E5F0AD2824B8B6CFB60E8DB226",
        "970FBA80489E81D9809B0ED805A46890CA252A7A94785D8842269128BA594910",
        "FFE286853413085336A29E7FC65A742F83C213AACAA71DDAD04500F172C9CC6E",
        "9DA234AA3E981BD4282C1A007A0343FBFE3BE5F32D697D6316EEB393C1EF9061",
        "9DE19A87E91E6EC60248711C460B52BD812109EEC3744085AEBFA24D2FCE4AC8",
        "FEC139F39399CF99557879EE89A63A284D734934DC4BAAC655C6E501AA55D75E"
    ];

    [Fact]
    public void Catalog_contains_exactly_the_sixteen_approved_variants()
    {
        Assert.Equal(16, EmailTemplateCatalog.Kinds.Count);
        Assert.Equal(16, Enum.GetValues<EmailTemplateKind>().Length);
        Assert.DoesNotContain("RenewalUpcoming", Enum.GetNames<EmailTemplateKind>());
        Assert.DoesNotContain("PlanRetired", Enum.GetNames<EmailTemplateKind>());
    }

    [Theory]
    [MemberData(nameof(TemplateCases))]
    public void Every_variant_matches_its_html_and_plain_text_snapshot(EmailTemplateKind kind, int index)
    {
        RenderedEmailTemplate rendered = EmailTemplateCatalog.Render(kind, SampleModel());
        string snapshot = $"{rendered.Subject}\n{rendered.HtmlBody}\n{rendered.PlainTextBody}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));

        Assert.Equal(ExpectedSnapshots[index], hash);
    }

    [Fact]
    public void Html_is_bilingual_arabic_first_rtl_and_encodes_caller_values()
    {
        var values = SampleValues();
        values["postTitle"] = "A <script>alert('x')</script> title";

        RenderedEmailTemplate rendered = EmailTemplateCatalog.Render(
            EmailTemplateKind.PostPublished,
            new EmailTemplateModel(values));

        Assert.Contains("lang=\"ar\" dir=\"rtl\"", rendered.HtmlBody);
        Assert.Contains("lang=\"en\" dir=\"ltr\"", rendered.HtmlBody);
        Assert.True(rendered.HtmlBody.IndexOf("dir=\"rtl\"", StringComparison.Ordinal) <
                    rendered.HtmlBody.IndexOf("dir=\"ltr\"", StringComparison.Ordinal));
        Assert.Contains("A &lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; title", rendered.HtmlBody);
        Assert.DoesNotContain("<script>", rendered.HtmlBody);
        Assert.Contains("Sanad Care", rendered.HtmlBody);
        Assert.Contains("Sanad Care", rendered.PlainTextBody);
    }

    [Fact]
    public void Missing_required_template_value_is_rejected()
    {
        var values = SampleValues();
        values.Remove("bookingId");

        Assert.Throws<ArgumentException>(() => EmailTemplateCatalog.Render(
            EmailTemplateKind.BookingPaid,
            new EmailTemplateModel(values)));
    }

    [Fact]
    public void Shared_layout_renders_arabic_before_english_and_encodes_event_copy()
    {
        RenderedEmailTemplate rendered = EmailTemplateCatalog.RenderBilingual(
            "عنوان عربي", "English subject", "عنوان عربي", "English heading",
            "رسالة <script>", "Message <script>");

        Assert.Contains("lang=\"ar\" dir=\"rtl\"", rendered.HtmlBody);
        Assert.Contains("lang=\"en\" dir=\"ltr\"", rendered.HtmlBody);
        Assert.True(rendered.HtmlBody.IndexOf("dir=\"rtl\"", StringComparison.Ordinal) <
                    rendered.HtmlBody.IndexOf("dir=\"ltr\"", StringComparison.Ordinal));
        Assert.Contains("&lt;script&gt;", rendered.HtmlBody);
        Assert.DoesNotContain("<script>", rendered.HtmlBody);
        Assert.Contains("رسالة <script>", rendered.PlainTextBody);
        Assert.Contains("Message <script>", rendered.PlainTextBody);
    }

    [Theory]
    [InlineData("تأكيد البريد الإلكتروني", "email verification")]
    [InlineData("إعادة تعيين كلمة المرور", "password reset")]
    public void Otp_template_preserves_the_verification_purpose(string purposeAr, string purposeEn)
    {
        var values = SampleValues();
        RenderedEmailTemplate rendered = EmailTemplateCatalog.Render(
            EmailTemplateKind.OtpCode,
            EmailTemplateModel.ForOtpCode(values, purposeAr, purposeEn));

        Assert.Contains(purposeAr, rendered.Subject);
        Assert.Contains(purposeEn, rendered.Subject);
        Assert.Contains(values["code"], rendered.PlainTextBody);
        Assert.Contains(values["expiresIn"], rendered.PlainTextBody);
    }

    [Fact]
    public void Upcoming_renewal_renders_distinct_copy_through_the_existing_subscription_renewed_kind()
    {
        RenderedEmailTemplate rendered = EmailTemplateCatalog.Render(
            EmailTemplateKind.SubscriptionRenewed,
            EmailTemplateModel.ForUpcomingSubscriptionRenewal(SampleValues()));
        string snapshot = $"{rendered.Subject}\n{rendered.HtmlBody}\n{rendered.PlainTextBody}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));

        Assert.Equal("BDF48B8461BD5619E2879E70C947C71DEB26299F4EE8221F905B563811F45D09", hash);
        Assert.Contains("Your subscription renewal is coming up", rendered.Subject);
        Assert.Contains("سيتم تجديد اشتراكك قريباً", rendered.PlainTextBody);
    }

    [Fact]
    public void Retired_plan_renders_distinct_copy_through_the_existing_subscription_changed_kind()
    {
        RenderedEmailTemplate rendered = EmailTemplateCatalog.Render(
            EmailTemplateKind.SubscriptionChanged,
            EmailTemplateModel.ForRetiredPlan(SampleValues()));
        string snapshot = $"{rendered.Subject}\n{rendered.HtmlBody}\n{rendered.PlainTextBody}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));

        Assert.Equal("E821EC00D72ABF955CB7006603345F096B3D9AA5DF4CD8F8D12B0FDC52179C2A", hash);
        Assert.Contains("Your plan has been retired", rendered.Subject);
        Assert.Contains("تم إيقاف خطتك السابقة", rendered.PlainTextBody);
    }

    public static IEnumerable<object[]> TemplateCases() =>
        Enum.GetValues<EmailTemplateKind>()
            .Select((kind, index) => new object[] { kind, index });

    private static EmailTemplateModel SampleModel() => new(SampleValues());

    private static Dictionary<string, string> SampleValues() => new(StringComparer.Ordinal)
    {
        ["code"] = "123456",
        ["expiresIn"] = "10 minutes",
        ["familyName"] = "Example family",
        ["inviteLink"] = "https://example.test/invite/abc",
        ["ticketId"] = "TICKET-123",
        ["subject"] = "Example support request",
        ["bookingId"] = "BOOKING-123",
        ["facilityName"] = "Example care home",
        ["date"] = "2026-10-10",
        ["refundAmount"] = "125.00",
        ["currency"] = "EGP",
        ["planName"] = "Standard",
        ["amount"] = "250.00",
        ["oldPlan"] = "Basic",
        ["payoutId"] = "PAYOUT-123",
        ["postTitle"] = "Example post",
        ["reason"] = "Please update the post"
    };
}
