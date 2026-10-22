using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.CareHomes.Domain.Bookings;

/// <summary>Which schedule produced a decision's share: the facility's own snapshotted terms, or the contract default.</summary>
public enum CareHomeRefundScheduleBasis
{
    /// <summary>Clause 2 default schedule (full at or before 24 hours before start, half inside it, nothing at or after).</summary>
    DefaultSchedule = 1,

    /// <summary>The facility's structured cancellation terms snapshotted at booking time (clause 6, decision #9).</summary>
    FacilityTerms = 2
}

/// <summary>
/// The amount-bearing outcome of a revision-2 cancellation decision.
/// <see cref="AwardAmount"/> is the refund the booking enters its refund
/// lifecycle with — zero awards are legitimate outcomes (the zero-refund
/// cancellation state), not errors (Owner decision #31's principle).
/// </summary>
public sealed record CareHomeRefundDecision(
    decimal AwardAmount,
    decimal Share,
    CareHomeRefundScheduleBasis Basis,
    CareHomeRefundPolicyVersion PolicyVersion);

/// <summary>
/// The Owner-approved care-home refund policy, revision 2, implemented from
/// binding clause 6 (care homes) with the clause 2 default schedule as
/// fallback, plus the extension-purchase rule of decision #8.
/// <para>
/// <b>Rules, measured against the scheduled stay start, never against facility
/// acceptance:</b>
/// <list type="bullet">
/// <item><description>Clause 6: the structured cancellation terms displayed on the facility page at booking time govern, exactly as snapshotted (decision #10: later facility changes never touch existing bookings).</description></item>
/// <item><description>Clause 6 fallback — clause 2: at or before 24 hours before the scheduled stay start, a full refund; strictly inside the 24-hour window, 50%; at or after the start (or when the recipient cannot be accessed at the appointed time), nothing.</description></item>
/// <item><description>Decision #8: a separately purchased extension snapshots the policy/terms applicable at its own purchase — it never inherits the root segment's snapshot.</description></item>
/// <item><description>Provider-side acts (facility cancellation, rejection, decision-window expiry) refund in full whatever the schedule — a facility failure is not an act the customer's timing can shrink (clause 4 principle preserved by the existing care-home amounts).</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Deterministic and side-effect-free.</b> The policy reads no clock, calls
/// no provider and queries nothing. It refuses to decide rather than guess: a
/// missing or non-UTC timestamp, an unreadable terms snapshot, a booking money
/// state it cannot reconcile, or an award outside the captured money all raise
/// <see cref="DomainException"/>. Uncertainty is never silently answered as
/// "no refund due".
/// </para>
/// <para>
/// <b>Money shape.</b> Care-home booking money is persisted as EGP major-unit
/// decimals at two places (the module's own convention), so awards are computed
/// as exact decimal shares of the captured total and rounded half to even at
/// two decimal places — the same rounding the Finance money core uses for
/// piastre conversions (decision #4/#37), applied at the module boundary.
/// Revision-1 bookings never reach this type; their acceptance-free,
/// check-in-relative amounts remain exactly as originally implemented.
/// </para>
/// </summary>
public static class CareHomeRefundPolicy
{
    /// <summary>Revision this policy implements.</summary>
    public const int CurrentPolicyVersion = CareHomeRefundPolicyVersion.ScheduledStartVersion;

    /// <summary>Clause 2: the window before the scheduled stay start in which cancellation is free.</summary>
    public static readonly TimeSpan FullRefundWindow = TimeSpan.FromHours(24);

    /// <summary>Clause 2: the share refunded strictly inside the 24-hour window.</summary>
    public const int WithinWindowRefundPercentage = 50;

    public static CareHomeRefundDecision Decide(CareHomeRefundPolicyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        input.Validate();

        CareHomeRefundPolicyVersion version = CareHomeRefundPolicyVersion.FromPersisted(
            CareHomeRefundPolicyVersion.ScheduledStartVersion,
            CareHomeRefundPolicyVersion.PolicyDocumentEffectiveOn);

        DateTime scheduledStart = CareHomeStayStart.ScheduledStartUtc(input.StartDate);
        decimal share;
        CareHomeRefundScheduleBasis basis;

        if (input.Terms is null)
        {
            share = DefaultShareFor(scheduledStart, input.ActedOnUtc);
            basis = CareHomeRefundScheduleBasis.DefaultSchedule;
        }
        else
        {
            share = input.Terms.ShareFor(scheduledStart, input.ActedOnUtc);
            basis = CareHomeRefundScheduleBasis.FacilityTerms;
        }

        if (share is < 0m or > 1m)
            throw new DomainException("A cancellation share must resolve between zero and one.");

        decimal award;
        if (input.CapturedTotalAmount <= 0m)
        {
            // Nothing captured: there is no money to move regardless of timing.
            // Reported as a zero award so the state can still be recorded.
            award = 0m;
        }
        else
        {
            award = decimal.Round(input.CapturedTotalAmount * share, 2, MidpointRounding.ToEven);

            // Decision #4: cumulative refunds can never exceed the captured
            // money less what was already refunded. With one refund lifecycle
            // per booking the ceiling is the captured total.
            award = Math.Clamp(award, 0m, input.CapturedTotalAmount);
        }

        return new CareHomeRefundDecision(award, share, basis, version);
    }

    /// <summary>
    /// The clause 2 default share for an act at <paramref name="actedOnUtc"/>
    /// against a stay starting at <paramref name="scheduledStartUtc"/>: full at
    /// or before 24 hours before the start, half strictly inside the window,
    /// nothing at or after the start.
    /// </summary>
    public static decimal DefaultShareFor(DateTime scheduledStartUtc, DateTime actedOnUtc)
    {
        CareHomeRefundPolicyInput.EnsureUtc(scheduledStartUtc, nameof(scheduledStartUtc));
        CareHomeRefundPolicyInput.EnsureUtc(actedOnUtc, nameof(actedOnUtc));

        if (actedOnUtc >= scheduledStartUtc)
            return 0m;
        if (actedOnUtc <= scheduledStartUtc - FullRefundWindow)
            return 1m;
        return WithinWindowRefundPercentage / 100m;
    }
}

/// <summary>
/// The scheduled start of a care-home stay. Care-home bookings are date-based
/// (a Cairo calendar date on the facility page), so the scheduled start is the
/// first instant of <see cref="ScheduledStartUtc"/>'s date in Africa/Cairo —
/// 00:00 local, converted to the UTC instant every refund clock compares
/// against (decision #1: Africa/Cairo local semantics, UTC persisted
/// instants).
/// <para>
/// Cairo's recent DST transitions land exactly on midnight, so the first
/// wall-clock reading of a start date can fall inside the spring-forward gap
/// (the reading does not exist) or, in the autumn, inside the repeated hour
/// (the reading is ambiguous). The gap is resolved like the documented
/// Finance pattern (<c>BusinessDayCalendar.LocalToUtc</c>): the wall clock is
/// shifted by the transition delta — measured from the zone's offsets on the
/// neighbouring days, so the arithmetic never depends on which reading was
/// invalid — and the day therefore begins at the first instant that actually
/// existed. An ambiguous midnight converts deterministically with the zone's
/// standard offset (the later occurrence). Both readings are pure functions
/// of the persisted start date: the same booking always resolves the same
/// scheduled start.
/// </para>
/// </summary>
public static class CareHomeStayStart
{
    public static DateTime ScheduledStartUtc(DateOnly startDate)
    {
        // The booking's own Cairo resolution: Windows tz id first, IANA fallback.
        TimeZoneInfo cairo = GetCairoTimeZone();
        DateTime unspecified = startDate.ToDateTime(TimeOnly.MinValue);

        if (cairo.IsInvalidTime(unspecified))
        {
            // Spring-forward gap: the requested wall clock does not exist.
            // Shift by the transition delta so the deadline is still valid —
            // the exact arithmetic BusinessDayCalendar.LocalToUtc documents.
            TimeSpan gap = cairo.GetUtcOffset(unspecified.AddDays(1))
                - cairo.GetUtcOffset(unspecified.AddDays(-1));
            unspecified = unspecified.Add(gap);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, cairo);
    }

    private static TimeZoneInfo GetCairoTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
    }
}

/// <summary>
/// The facts one revision-2 cancellation act is decided from. Carried by the
/// <see cref="CareHomeBooking"/> methods (which own the state guards) so the
/// policy itself stays a pure function over persisted values.
/// </summary>
public sealed record CareHomeRefundPolicyInput(
    DateOnly StartDate,
    DateTime ActedOnUtc,
    decimal CapturedTotalAmount,
    CareHomeCancellationTerms? Terms)
{
    public void Validate()
    {
        EnsureUtc(ActedOnUtc, "Cancellation time");
        if (CapturedTotalAmount < 0m)
            throw new DomainException("Captured money cannot be a negative amount.");
        if (StartDate == default)
            throw new DomainException("A scheduled stay start date is required.");
    }

    internal static void EnsureUtc(DateTime value, string field)
    {
        if (value == default)
            throw new DomainException($"{field} is required.");

        if (value.Kind != DateTimeKind.Utc)
            throw new DomainException($"{field} must be a UTC timestamp.");
    }
}
