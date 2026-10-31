using System.Globalization;

namespace Ahlena.Modules.Finance.Domain;

/// <summary>
/// Business-day arithmetic for the refund policy deadlines (Owner decision #1
/// and #2: the initial-response clock and the seven-business-day
/// refund-processing clock). The business week is Sunday–Thursday with Friday
/// and Saturday as the weekend, evaluated in the Africa/Cairo time zone, and
/// UTC instants are the only persisted representation.
/// <para>
/// <b>Time zone resolution.</b> Egypt reinstated daylight saving time in 2023,
/// so the calendar must follow the real DST rules rather than a fixed offset.
/// .NET resolves <c>Africa/Cairo</c> on Linux/macOS and its Windows equivalent
/// <c>Egypt Standard Time</c> on Windows. The sibling modules disagree on
/// preference: <c>CareHomeVisitScheduleRules</c> tries the IANA id first and
/// falls back to the Windows id, while <c>CareHomeMaintenanceMutationHandlers</c>
/// tries the Windows id first. This type follows the IANA-first order — the
/// IANA database is the authority the policy names, and .NET maps it to the
/// platform zone, so the DST rules are identical on every supported host.
/// </para>
/// <para>
/// <b>Public holidays.</b> Owner decision #36 excludes official Egyptian public
/// holidays from the deadline clocks: the fixed-date holidays (Coptic
/// Christmas, Revolution days, Sinai Liberation, Labour Day, Armed Forces Day),
/// the moveable Coptic feasts (Coptic Easter and Sham El-Nessim, computed with
/// the Julian computus), and the Islamic feasts (Eid al-Fitr, Arafat Day and
/// Eid al-Adha, Hijri New Year, Mawlid — read from the Umm al-Qura tables).
/// Lunar feast dates depend on moon sighting: government announcements may
/// shift a computed date by about a day, and the announced date always wins —
/// add it to <see cref="AnnouncedHolidays"/> at startup (an optional extra
/// holiday, never a removal). The seven-business-day horizon therefore moves
/// with holidays automatically through <see cref="IsBusinessDay(DateOnly)"/>.
/// </para>
/// </summary>
public static class BusinessDayCalendar
{
    public const string CairoIanaTimeZoneId = "Africa/Cairo";
    public const string CairoWindowsTimeZoneId = "Egypt Standard Time";

    /// <summary>Cairo's working week. Friday and Saturday are the weekend.</summary>
    public static readonly IReadOnlyList<DayOfWeek> BusinessDays =
    [
        DayOfWeek.Sunday,
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday
    ];

    private static readonly Lazy<TimeZoneInfo> CairoZone = new(ResolveCairoTimeZone);

    private static TimeZoneInfo ResolveCairoTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(CairoIanaTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(CairoWindowsTimeZoneId);
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(CairoWindowsTimeZoneId);
        }
    }

    /// <summary>The resolved Africa/Cairo zone (real DST rules, not a fixed offset).</summary>
    public static TimeZoneInfo Cairo => CairoZone.Value;

    /// <summary>
    /// Government-announced holiday dates that differ from the computed lunar
    /// feasts (typically about a day, by moon sighting). Adding a date here
    /// makes it a non-business day; nothing is ever removed. Mutate only during
    /// application startup, before any deadline is computed.
    /// </summary>
    public static readonly HashSet<DateOnly> AnnouncedHolidays = [];

    private static readonly HashSet<(int Month, int Day)> FixedGregorianHolidays =
    [
        (1, 7),   // Coptic Christmas
        (1, 25),  // January 25 Revolution Day
        (4, 25),  // Sinai Liberation Day
        (5, 1),   // Labour Day
        (7, 23),  // July 23 Revolution Day
        (10, 6)   // Armed Forces Day
    ];

    private static readonly UmAlQuraCalendar Hijri = new();

    public static bool IsBusinessDay(DateOnly date) =>
        BusinessDays.Contains(date.DayOfWeek) && !IsPublicHoliday(date);

    /// <summary>
    /// True on an official Egyptian public holiday (Owner decision #36) even if
    /// the date falls on a normally-business weekday.
    /// </summary>
    public static bool IsPublicHoliday(DateOnly date) =>
        AnnouncedHolidays.Contains(date) ||
        FixedGregorianHolidays.Contains((date.Month, date.Day)) ||
        IsCopticFeast(date) ||
        IsIslamicFeast(date);

    /// <summary>
    /// Coptic Easter (computed by the Julian computus, converted to the
    /// Gregorian date it is observed on) and Sham El-Nessim, the Monday that
    /// immediately follows it.
    /// </summary>
    private static bool IsCopticFeast(DateOnly date)
    {
        DateOnly easter = CopticEaster(date.Year);
        return date == easter || date == easter.AddDays(1);
    }

    /// <summary>
    /// Orthodox (Julian) Easter for the given Gregorian year, via the Meeus
    /// Julian algorithm; <see cref="JulianCalendar.ToDateTime"/> performs the
    /// Julian-to-Gregorian conversion.
    /// </summary>
    public static DateOnly CopticEaster(int gregorianYear)
    {
        int a = gregorianYear % 4;
        int b = gregorianYear % 7;
        int c = gregorianYear % 19;
        int d = (19 * c + 15) % 30;
        int e = (2 * a + 4 * b - d + 34) % 7;
        int month = (d + e + 114) / 31;
        int day = (d + e + 114) % 31 + 1;

        DateTime gregorian = new JulianCalendar().ToDateTime(gregorianYear, month, day, 0, 0, 0, 0);
        return DateOnly.FromDateTime(gregorian);
    }

    /// <summary>
    /// The Islamic feasts, read from the Umm al-Qura tables (the closest
    /// published lunar tables to Egypt's observance): Eid al-Fitr (Shawwal
    /// 1–3), Arafat Day (Dhul-Hijjah 9) with Eid al-Adha (10–12), Hijri New
    /// Year (Muharram 1) and Mawlid an-Nabi (Rabi' al-Awwal 12). Announced
    /// dates that shift by sighting are appended to <see cref="AnnouncedHolidays"/>.
    /// </summary>
    private static bool IsIslamicFeast(DateOnly date)
    {
        DateTime gregorian = date.ToDateTime(TimeOnly.MinValue);
        if (gregorian.Year is < 1900 or > 2077 || !IsWellFormedHijri(gregorian))
            return false;

        int month = Hijri.GetMonth(gregorian);
        int day = Hijri.GetDayOfMonth(gregorian);

        return (month, day) switch
        {
            (10, 1) or (10, 2) or (10, 3) => true,  // Eid al-Fitr (Shawwal 1-3)
            (12, 9) => true,                        // Arafat Day
            (12, 10) or (12, 11) or (12, 12) => true, // Eid al-Adha
            (1, 1) => true,                         // Hijri New Year
            (3, 12) => true,                        // Mawlid an-Nabi
            _ => false
        };
    }

    private static bool IsWellFormedHijri(DateTime gregorian)
    {
        try
        {
            Hijri.GetYear(gregorian);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }


    /// <summary>Cairo-local calendar date of a UTC instant.</summary>
    public static DateOnly ToCairoDate(DateTime utcInstant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utcInstant), Cairo));

    /// <summary>
    /// Adds business days to a UTC instant. Same-business-day preservation is
    /// intentional: the deadline keeps the instant's Cairo wall-clock time, and
    /// outside a DST transition the UTC time of day is preserved too. When the
    /// target wall-clock time does not exist locally (the spring-forward gap,
    /// which in Cairo falls within a business day) the instant is shifted
    /// forward by the gap so the result is still a valid, strictly-later
    /// instant.
    /// <para>
    /// The starting instant itself is never moved: if it falls on a weekend or
    /// outside business hours its deadline is still counted from that instant.
    /// Only the day counter advances.
    /// </para>
    /// </summary>
    public static DateTime AddBusinessDays(DateTime utcInstant, int businessDays)
    {
        DateTime utc = AsUtc(utcInstant);
        if (businessDays == 0)
            return utc;

        int remaining = Math.Abs(businessDays);
        int direction = businessDays > 0 ? 1 : -1;
        DateTime cursor = utc;
        while (remaining > 0)
        {
            DateOnly localDate = ToCairoDate(cursor).AddDays(direction);
            DateTime localNext = localDate.ToDateTime(TimeOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(cursor, Cairo)));
            cursor = LocalToUtc(localNext);
            if (IsBusinessDay(localDate))
                remaining--;
        }

        return cursor;
    }

    /// <summary>
    /// The next business day strictly after the given UTC instant's Cairo date,
    /// at the same local wall-clock time.
    /// </summary>
    public static DateTime NextBusinessDay(DateTime utcInstant)
    {
        DateTime utc = AsUtc(utcInstant);
        DateOnly localDate = ToCairoDate(utc);
        TimeOnly localTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Cairo));

        DateOnly candidate = localDate.AddDays(1);
        while (!IsBusinessDay(candidate))
            candidate = candidate.AddDays(1);

        return LocalToUtc(candidate.ToDateTime(localTime));
    }

    /// <summary>
    /// Cairo-local midnight of the day after the given Cairo date, skipping the
    /// weekend. Used when a deadline is expressed as a whole business date
    /// rather than a preserved instant (for example the "seven business days
    /// from approval" horizon read as a date).
    /// </summary>
    public static DateOnly AddBusinessDays(DateOnly cairoDate, int businessDays)
    {
        int remaining = Math.Abs(businessDays);
        int direction = businessDays >= 0 ? 1 : -1;
        DateOnly cursor = cairoDate;
        while (remaining > 0)
        {
            cursor = cursor.AddDays(direction);
            if (IsBusinessDay(cursor))
                remaining--;
        }

        return cursor;
    }

    /// <summary>Counts business days in the half-open range [start, end): the end date is never counted.</summary>
    public static int BusinessDaysBetween(DateOnly start, DateOnly end)
    {
        if (end <= start)
            return 0;

        int count = 0;
        for (DateOnly cursor = start; cursor < end; cursor = cursor.AddDays(1))
        {
            if (IsBusinessDay(cursor))
                count++;
        }

        return count;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime LocalToUtc(DateTime local)
    {
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (Cairo.IsInvalidTime(unspecified))
        {
            // Spring-forward gap: the requested wall clock does not exist.
            // Shift by the transition delta so the deadline is still valid.
            TimeSpan gap = Cairo.GetUtcOffset(unspecified.AddDays(1)) -
                Cairo.GetUtcOffset(unspecified.AddDays(-1));
            unspecified = unspecified.Add(gap);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Cairo);
    }
}
