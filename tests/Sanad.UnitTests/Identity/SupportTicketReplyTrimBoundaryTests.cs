using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.UnitTests.Identity;

/// <summary>
/// Consumed-text cap facts for the support reply body: the 1–2000 rule in
/// <see cref="SupportTicketReply.Create"/> is measured on the TRIMMED text, so
/// an over-long RAW body that trims inside the cap is accepted (and stored
/// trimmed). This exact acceptance is why the OpenAPI request schema publishes
/// no raw <c>maxLength</c>/<c>minLength</c>: either annotation would reject
/// previously accepted inputs (schema pinned by
/// <c>RequestLengthBoundOpenApiContractTests</c>).
/// </summary>
public sealed class SupportTicketReplyTrimBoundaryTests
{
    private static SupportTicketReply Create(string body) => SupportTicketReply.Create(
        new SupportTicketId(Guid.NewGuid()), UserId.New(), body,
        new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Padded_body_whose_trimmed_length_is_exactly_2000_is_accepted_and_stored_trimmed()
    {
        // Raw 2001 characters; trimmed exactly 2000. A raw maxLength: 2000
        // annotation would have refused this previously accepted input.
        SupportTicketReply reply = Create(new string('x', 2000) + ' ');

        Assert.Equal(2000, reply.Body.Length);
        Assert.Equal(new string('x', 2000), reply.Body);
    }

    [Fact]
    public void Short_padded_body_is_accepted_and_stored_trimmed()
    {
        SupportTicketReply reply = Create("  We are looking into this.  ");

        Assert.Equal("We are looking into this.", reply.Body);
    }

    [Theory]
    [InlineData(2001)]
    [InlineData(3000)]
    public void Body_whose_trimmed_length_exceeds_2000_is_refused(int trimmedLength)
    {
        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(
            () => Create(new string('x', trimmedLength)));
    }

    [Fact]
    public void Whitespace_only_body_is_refused_by_the_existing_trimmed_minimum()
    {
        // Prior behavior preserved: this reaches the domain guard and throws;
        // the API adds no raw schema minLength that could change any outcome.
        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(() => Create("   "));
    }
}
