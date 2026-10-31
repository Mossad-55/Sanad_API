using System.Security.Cryptography;
using System.Text;
using Ahlena.Modules.Identity.Infrastructure.Messaging.EmailTemplates;

namespace Ahlena.UnitTests.Identity.Infrastructure;

public sealed class EmailTemplateTests
{
    private static readonly string[] ExpectedSnapshots =
    [
        "CFA685BB83CA4EE1E24E604DEEDFC04C521D11E0226A68985D7BA8A080C5FD04",
        "406B2C4A4011E70CDD08E423B61D3AF5A00E8ECB3499ACA9366D5AB384C66DD1",
        "256025B92F920F2E3370384AFEA78F30A10A608C29386B9C625F582F731F9183",
        "506BAC833D9EAC5C12E6EC78F822CDBEE17305652A6EE31E3024301FC0938971",
        "70F37EDDB5CD1BCE2ADCBCE40C0CE30870626301CBC04E78FDA9C717B22063E4",
        "02473C06483F89055DC026513A7C458A289C280625FDE31BD825065EC9982710",
        "B615A5E6923F7C8922DDE017804D4C6D87F387ADDD23129E0AC7112298E15D76",
        "574A81E5BDA4E1900FE82CA31489CADFC4009E94EE8C2B95BB9E2FAC054CA97B",
        "3401A0B0C1747745E44D29B654DA60DA223FFED2437F69449B77115853273F81",
        "CB78A44482D3000EBCBD1840BFDD799EF58DFE8903775B970BF5D0E5C217CB8D",
        "34325679E12BBDCE23A5D2B8917C1F8A7DA07CCA2B3B4FA708BD5358DFC3716F",
        "D818C02F9D1A6B4A8E8FBFF4DBF790BB8390D9309ECEBC02E460F8DE40A91D76",
        "608381A7023D9E0660E5BE89D7F4DAAE7252C5BF6F54A988CD0CD3474E8D4001",
        "22F5583101BF8EB3A320657B619D8C62A2D005D4FE4A53AC9F2B1C90C28794E2",
        "011F46C46C4B8C2731932F4FC4829ADE84DFC5CBFD00F8DF8BA1A6F6F086B7C1",
        "FDD5D7274E31798EC4274ED51B0CB3F4D7D980A3BA37F88BC3BFB8B2E6C7B480"
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
        Assert.Contains("Ahlena", rendered.HtmlBody);
        Assert.Contains("Ahlena", rendered.PlainTextBody);
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

        Assert.Equal("FBE8120E04B5F2E28AE034B05FE5CB954722D8AFA25A98085C9F2C3E3C2AF61F", hash);
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

        Assert.Equal("9BC4810B364C0CB70F36A59CD330382243AFFBEC8F221695804BD687BE7F8602", hash);
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
