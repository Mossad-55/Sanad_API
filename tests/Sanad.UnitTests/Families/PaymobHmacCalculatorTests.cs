using System.Text.Json;
using Sanad.Modules.Families.Infrastructure.Payments;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class PaymobHmacCalculatorTests
{
    private const string Secret = "test-hmac-secret";

    private const string ExpectedHmac =
        "27d715d42be3f250c758103dbd3cba6c54f0f7e862b996478add4f848dd1c605424049fcabc5bed93d193fa89823d0c982ab20af76cb8dcc677fdaa0119461d4";

    private static JsonElement SampleObj() =>
        JsonDocument.Parse(
            """
            {
              "id": 999888,
              "amount_cents": 57500,
              "created_at": "2026-09-05T10:00:00",
              "currency": "EGP",
              "error_occured": null,
              "has_parent_transaction": false,
              "integration_id": 4586683,
              "is_3d_secure": true,
              "is_auth": false,
              "is_capture": false,
              "is_refunded": false,
              "is_standalone_payment": false,
              "is_voided": false,
              "order": { "id": 555444, "merchant_order_id": "b2f0c9d4-0000-0000-0000-000000000000" },
              "owner": null,
              "pending": false,
              "source_data": { "pan": "2346", "sub_type": "MasterCard", "type": "card" },
              "success": true
            }
            """).RootElement.Clone();

    [Fact]
    public void Calculate_ShouldProduceOfficialConcatenationHmac()
    {
        Assert.Equal(ExpectedHmac, PaymobHmacCalculator.Calculate(SampleObj(), Secret));
    }

    [Fact]
    public void IsValid_ShouldAcceptQueryHex_CaseAnd0xPrefix()
    {
        JsonElement obj = SampleObj();

        Assert.True(PaymobHmacCalculator.IsValid(obj, Secret, ExpectedHmac));
        Assert.True(PaymobHmacCalculator.IsValid(obj, Secret, ExpectedHmac.ToUpperInvariant()));
        Assert.True(PaymobHmacCalculator.IsValid(obj, Secret, "0x" + ExpectedHmac));
        Assert.False(PaymobHmacCalculator.IsValid(obj, Secret, "00" + ExpectedHmac[2..]));
        Assert.False(PaymobHmacCalculator.IsValid(obj, Secret, null));
        Assert.False(PaymobHmacCalculator.IsValid(obj, Secret, "not-hex"));
    }

    [Fact]
    public void CoalesceProvidedHmac_ShouldPreferQuery_ThenBody_ThenHeader()
    {
        using var bodyWithHmac = JsonDocument.Parse("""{ "hmac": "from-body", "obj": {} }""");
        using var bodyNoHmac = JsonDocument.Parse("""{ "obj": {} }""");

        Assert.Equal(
            "from-query",
            PaymobHmacCalculator.CoalesceProvidedHmac("from-query", bodyWithHmac.RootElement, "from-header"));

        Assert.Equal(
            "from-body",
            PaymobHmacCalculator.CoalesceProvidedHmac(null, bodyWithHmac.RootElement, "from-header"));

        Assert.Equal(
            "from-header",
            PaymobHmacCalculator.CoalesceProvidedHmac(null, bodyNoHmac.RootElement, "from-header"));
    }

    [Fact]
    public void Calculate_ShouldTreatNullsAsEmpty_AndDifferFromFilledValues()
    {
        var withOwner = JsonDocument.Parse(
            """
            { "amount_cents": 100, "owner": "someone" }
            """).RootElement;

        var withoutOwner = JsonDocument.Parse(
            """
            { "amount_cents": 100, "owner": null }
            """).RootElement;

        Assert.NotEqual(
            PaymobHmacCalculator.Calculate(withOwner, Secret),
            PaymobHmacCalculator.Calculate(withoutOwner, Secret));
    }
}
