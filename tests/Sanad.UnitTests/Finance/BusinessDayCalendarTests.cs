using Sanad.Modules.Finance.Domain;

namespace Sanad.UnitTests.Finance;

/// <summary>
/// W2-F0 money core: the Africa/Cairo business-day calendar behind the refund
/// policy's two-business-day and seven-business-day clocks (Owner decisions #1,
/// #2 and #36). Run green at the W2 integration gate by the Mastermind.
/// </summary>
public sealed class BusinessDayCalendarTests
{
    [Fact]
    public void African_cairo_zone_resolves_with_real_dst_rules_not_a_fixed_offset()
    {
        TimeZoneInfo cairo = BusinessDayCalendar.Cairo;

        Assert.Equal("Africa/Cairo", cairo.Id);

        // Egypt reinstated DST in 2023. The zone must therefore show a non-zero
        // summer offset shift relative to winter, which a fixed +02:00 zone
        // could never produce.
        TimeSpan winter = cairo.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified));
        TimeSpan summer = cairo.GetUtcOffset(new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Unspecified));

        Assert.NotEqual(winter, summer);
        Assert.True(summer > winter);
    }

    [Theory]
    [InlineData(2026, 10, 18, DayOfWeek.Sunday, true)]
    [InlineData(2026, 10, 19, DayOfWeek.Monday, true)]
    [InlineData(2026, 10, 20, DayOfWeek.Tuesday, true)]
    [InlineData(2026, 10, 21, DayOfWeek.Wednesday, true)]
    [InlineData(2026, 10, 22, DayOfWeek.Thursday, true)]
    [InlineData(2026, 10, 23, DayOfWeek.Friday, false)]
    [InlineData(2026, 10, 24, DayOfWeek.Saturday, false)]
    public void Business_week_is_sunday_through_thursday(
        int year,
        int month,
        int day,
        DayOfWeek expectedDayOfWeek,
        bool expectedIsBusinessDay)
    {
        DateOnly date = new(year, month, day);

        Assert.Equal(expectedDayOfWeek, date.DayOfWeek);
        Assert.Equal(expectedIsBusinessDay, BusinessDayCalendar.IsBusinessDay(date));
    }

    [Fact]
    public void Business_day_set_is_exactly_the_five_working_weekdays()
    {
        Assert.Equal(
            [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday],
            BusinessDayCalendar.BusinessDays);
        Assert.DoesNotContain(DayOfWeek.Friday, BusinessDayCalendar.BusinessDays);
        Assert.DoesNotContain(DayOfWeek.Saturday, BusinessDayCalendar.BusinessDays);
    }

    [Fact]
    public void Adding_business_days_skips_the_friday_saturday_weekend()
    {
        // Thursday 2026-10-22 + 1 business day must land on Sunday 2026-10-25,
        // not Friday 2026-10-23.
        DateTime thursday = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);

        DateTime plusOne = BusinessDayCalendar.AddBusinessDays(thursday, 1);

        Assert.Equal(new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc), plusOne);
        Assert.Equal(DayOfWeek.Sunday, BusinessDayCalendar.ToCairoDate(plusOne).DayOfWeek);
    }

    [Fact]
    public void Adding_zero_business_days_returns_the_same_instant()
    {
        DateTime instant = new(2026, 10, 23, 15, 30, 0, DateTimeKind.Utc); // a Friday

        Assert.Equal(instant, BusinessDayCalendar.AddBusinessDays(instant, 0));
    }

    [Fact]
    public void Adding_one_business_day_from_a_weekend_starts_from_the_next_working_day()
    {
        // Saturday 2026-10-24 + 1 business day -> Sunday 2026-10-25.
        DateTime saturday = new(2026, 10, 24, 8, 15, 0, DateTimeKind.Utc);

        DateTime result = BusinessDayCalendar.AddBusinessDays(saturday, 1);

        Assert.Equal(new DateTime(2026, 10, 25, 8, 15, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void Seven_business_days_from_a_thursday_lands_one_week_and_two_days_later()
    {
        // Thursday 2026-10-22 + 7 business days: Thu->Sun(1) Mon(2) Tue(3)
        // Wed(4) Thu(5) Sun(6) Mon(7) = Monday 2026-11-02. This hop crosses
        // Egypt's DST end (late October 2026), so the UTC instant depends on
        // the host's tz rules; the invariant must hold under any correct
        // Africa/Cairo data, so pin the structure: Monday 2026-11-02, same
        // Cairo wall-clock time as the start.
        DateTime thursday = new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
        TimeOnly startLocalWall = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(thursday, BusinessDayCalendar.Cairo));

        DateTime result = BusinessDayCalendar.AddBusinessDays(thursday, 7);

        Assert.Equal(new DateOnly(2026, 11, 2), BusinessDayCalendar.ToCairoDate(result));
        Assert.Equal(DayOfWeek.Monday, BusinessDayCalendar.ToCairoDate(result).DayOfWeek);
        Assert.Equal(startLocalWall, TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(result, BusinessDayCalendar.Cairo)));
        Assert.True(result > thursday);
    }

    [Fact]
    public void Two_business_days_from_a_thursday_crosses_the_weekend()
    {
        DateTime thursday = new(2026, 10, 22, 11, 45, 0, DateTimeKind.Utc);

        DateTime result = BusinessDayCalendar.AddBusinessDays(thursday, 2);

        // Sun(1) Mon(2) = Monday 2026-10-26.
        Assert.Equal(new DateTime(2026, 10, 26, 11, 45, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void Negative_business_day_offsets_move_backwards_and_can_skip_the_weekend()
    {
        // Sunday 2026-10-25 - 1 business day -> Thursday 2026-10-22 (skips Fri/Sat).
        DateTime sunday = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);

        DateTime result = BusinessDayCalendar.AddBusinessDays(sunday, -1);

        Assert.Equal(new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc), result);
        Assert.Equal(DayOfWeek.Thursday, BusinessDayCalendar.ToCairoDate(result).DayOfWeek);
    }

    [Fact]
    public void Next_business_day_skips_a_weekend()
    {
        // Thursday -> Sunday.
        Assert.Equal(
            new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc),
            BusinessDayCalendar.NextBusinessDay(new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc)));

        // Friday -> Sunday (two days on).
        Assert.Equal(
            new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc),
            BusinessDayCalendar.NextBusinessDay(new DateTime(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc)));

        // Saturday -> Sunday (one day on).
        Assert.Equal(
            new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc),
            BusinessDayCalendar.NextBusinessDay(new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc)));

        // Sunday -> Monday (never stays on the start day).
        Assert.Equal(
            new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc),
            BusinessDayCalendar.NextBusinessDay(new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Date_based_add_business_days_skips_weekends()
    {
        Assert.Equal(new DateOnly(2026, 10, 25), BusinessDayCalendar.AddBusinessDays(new DateOnly(2026, 10, 22), 1));
        Assert.Equal(new DateOnly(2026, 10, 26), BusinessDayCalendar.AddBusinessDays(new DateOnly(2026, 10, 22), 2));
        Assert.Equal(new DateOnly(2026, 11, 2), BusinessDayCalendar.AddBusinessDays(new DateOnly(2026, 10, 22), 7));
        Assert.Equal(new DateOnly(2026, 10, 22), BusinessDayCalendar.AddBusinessDays(new DateOnly(2026, 10, 22), 0));
    }

    [Fact]
    public void Counting_business_days_excludes_the_weekend()
    {
        // A full Cairo week (Sun..Sat) contains exactly five business days.
        Assert.Equal(5, BusinessDayCalendar.BusinessDaysBetween(new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 25)));

        // Thursday -> Sunday is one business day.
        Assert.Equal(1, BusinessDayCalendar.BusinessDaysBetween(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 25)));

        // Thursday -> Monday is two.
        Assert.Equal(2, BusinessDayCalendar.BusinessDaysBetween(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 26)));

        Assert.Equal(0, BusinessDayCalendar.BusinessDaysBetween(new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 25)));
        Assert.Equal(0, BusinessDayCalendar.BusinessDaysBetween(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22)));
        Assert.Equal(0, BusinessDayCalendar.BusinessDaysBetween(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 22)));
    }

    [Fact]
    public void Cairo_date_conversion_uses_the_local_calendar_day_not_the_utc_day()
    {
        // 2026-10-22T23:30Z is already 2026-10-23 (Friday) in Cairo summer time
        // (+03:00), so it is NOT a business day.
        Assert.Equal(
            new DateOnly(2026, 10, 23),
            BusinessDayCalendar.ToCairoDate(new DateTime(2026, 10, 22, 23, 30, 0, DateTimeKind.Utc)));
        Assert.False(BusinessDayCalendar.IsBusinessDay(
            BusinessDayCalendar.ToCairoDate(new DateTime(2026, 10, 22, 23, 30, 0, DateTimeKind.Utc))));
    }

    [Fact]
    public void Dst_spring_forward_near_april_produces_a_valid_later_instant()
    {
        // Cairo's spring-forward transition lands in April and can affect the
        // preserved wall-clock time of a business-day deadline. The result must
        // still be a valid, strictly later instant in the same zone.
        DateOnly transitionDate = AprilTransitionDate(2026);
        DateTime beforeGap = new DateTime(
            transitionDate.Year, transitionDate.Month, transitionDate.Day, 1, 30, 0, DateTimeKind.Utc);

        DateTime result = BusinessDayCalendar.AddBusinessDays(beforeGap, 1);

        Assert.True(result > beforeGap);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(result, BusinessDayCalendar.Cairo);
        Assert.False(BusinessDayCalendar.Cairo.IsInvalidTime(DateTime.SpecifyKind(local, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Dst_fall_back_near_october_produces_a_valid_later_instant()
    {
        DateOnly transitionDate = OctoberTransitionDate(2026);
        DateTime aroundTransition = new DateTime(
            transitionDate.Year, transitionDate.Month, transitionDate.Day, 20, 0, 0, DateTimeKind.Utc);

        DateTime result = BusinessDayCalendar.AddBusinessDays(aroundTransition, 1);

        Assert.True(result > aroundTransition);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void Business_day_arithmetic_is_monotonic_across_a_dst_transition()
    {
        DateOnly transitionDate = AprilTransitionDate(2026);
        DateTime start = new DateTime(
            transitionDate.Year, transitionDate.Month, transitionDate.Day, 6, 0, 0, DateTimeKind.Utc);

        DateTime previous = start;
        for (int i = 1; i <= 5; i++)
        {
            DateTime next = BusinessDayCalendar.AddBusinessDays(start, i);
            Assert.True(next > previous, $"Business day {i} must be strictly later than {i - 1}.");
            previous = next;
        }
    }

    [Fact]
    public void Local_dates_and_instants_keep_the_cairo_wall_clock_time_when_that_time_exists()
    {
        DateTime thursday = new(2026, 10, 22, 14, 20, 0, DateTimeKind.Utc);

        DateTime result = BusinessDayCalendar.AddBusinessDays(thursday, 1);

        TimeOnly originalLocalTime = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(thursday, BusinessDayCalendar.Cairo));
        TimeOnly resultLocalTime = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(result, BusinessDayCalendar.Cairo));

        Assert.Equal(originalLocalTime, resultLocalTime);
    }

    [Theory]
    [InlineData(2026, 1, 7, "Coptic Christmas")]
    [InlineData(2026, 1, 25, "January 25 Revolution Day")]
    [InlineData(2026, 4, 25, "Sinai Liberation Day")]
    [InlineData(2026, 5, 1, "Labour Day")]
    [InlineData(2026, 7, 23, "July 23 Revolution Day")]
    [InlineData(2026, 10, 6, "Armed Forces Day")]
    public void Fixed_gregorian_holidays_are_not_business_days(int year, int month, int day, string holidayName)
    {
        DateOnly date = new(year, month, day);

        Assert.True(BusinessDayCalendar.IsPublicHoliday(date), holidayName);
        Assert.False(BusinessDayCalendar.IsBusinessDay(date), holidayName);
    }

    [Theory]
    [InlineData(2024, 5, 5)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 12)]
    public void Coptic_easter_follows_the_julian_computus(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), BusinessDayCalendar.CopticEaster(year));
    }

    [Fact]
    public void Sham_el_nessim_is_the_monday_after_coptic_easter()
    {
        DateOnly sham2026 = new(2026, 4, 13);

        Assert.Equal(BusinessDayCalendar.CopticEaster(2026).AddDays(1), sham2026);
        Assert.True(BusinessDayCalendar.IsPublicHoliday(sham2026));
        Assert.False(BusinessDayCalendar.IsBusinessDay(sham2026));
        Assert.True(BusinessDayCalendar.IsBusinessDay(new DateOnly(2026, 4, 14)));
    }

    [Theory]
    [InlineData(2019, 6, 4, "Eid al-Fitr 1440 Shawwal 1")]
    [InlineData(2026, 3, 20, "Eid al-Fitr 1447 Shawwal 1")]
    [InlineData(2026, 3, 21, "Eid al-Fitr 1447 Shawwal 2")]
    [InlineData(2026, 5, 26, "Arafat Day 1447 Dhul-Hijjah 9")]
    [InlineData(2026, 5, 27, "Eid al-Adha 1447 Dhul-Hijjah 10")]
    [InlineData(2026, 8, 25, "Mawlid an-Nabi 1448 Rabi al-Awwal 12")]
    public void Islamic_feasts_read_from_the_lunar_tables_are_holidays(
        int year, int month, int day, string feastName)
    {
        DateOnly date = new(year, month, day);

        Assert.True(BusinessDayCalendar.IsPublicHoliday(date), feastName);
        Assert.False(BusinessDayCalendar.IsBusinessDay(date), feastName);
    }

    [Fact]
    public void A_plain_weekday_around_the_feasts_stays_a_business_day()
    {
        // 2026-03-19 (Thursday) is the day before Eid al-Fitr 2026; 2026-06-10
        // (Wednesday) sits after the Eid al-Adha block, before Hijri New Year.
        Assert.True(BusinessDayCalendar.IsBusinessDay(new DateOnly(2026, 3, 19)));
        Assert.True(BusinessDayCalendar.IsBusinessDay(new DateOnly(2026, 6, 10)));
        Assert.Equal(DayOfWeek.Thursday, new DateOnly(2026, 3, 19).DayOfWeek);
    }

    [Fact]
    public void An_announced_holiday_that_shifts_a_lunar_feast_is_honored()
    {
        // Moon sighting can shift a computed feast by about a day; the
        // announced date always wins (Owner decision #36 override seam).
        DateOnly announced = new(2026, 3, 19);
        Assert.True(BusinessDayCalendar.IsBusinessDay(announced));

        try
        {
            BusinessDayCalendar.AnnouncedHolidays.Add(announced);
            Assert.True(BusinessDayCalendar.IsPublicHoliday(announced));
            Assert.False(BusinessDayCalendar.IsBusinessDay(announced));
        }
        finally
        {
            BusinessDayCalendar.AnnouncedHolidays.Remove(announced);
        }

        Assert.True(BusinessDayCalendar.IsBusinessDay(announced));
    }

    [Fact]
    public void Business_day_arithmetic_skips_holidays()
    {
        // Monday 2026-10-05 + 1 business day: Tuesday 2026-10-06 is Armed
        // Forces Day, so the deadline lands on Wednesday 2026-10-07.
        Assert.Equal(new DateOnly(2026, 10, 7), BusinessDayCalendar.AddBusinessDays(new DateOnly(2026, 10, 5), 1));

        // The holiday occupies a weekday slot but is not counted.
        Assert.Equal(1, BusinessDayCalendar.BusinessDaysBetween(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7)));

        // The instant-based clock skips the holiday too, preserving wall clock.
        DateTime monday = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);
        DateTime result = BusinessDayCalendar.AddBusinessDays(monday, 1);
        Assert.Equal(new DateOnly(2026, 10, 7), BusinessDayCalendar.ToCairoDate(result));
    }

    private static DateOnly AprilTransitionDate(int year)
    {
        for (int day = 1; day <= 30; day++)
        {
            DateOnly date = new(year, 4, day);
            if (BusinessDayCalendar.Cairo.IsDaylightSavingTime(new DateTime(year, 4, day, 12, 0, 0, DateTimeKind.Unspecified)))
            {
                for (int hour = 0; hour < 24; hour++)
                {
                    DateTime candidate = new(year, 4, day, hour, 0, 0, DateTimeKind.Unspecified);
                    if (BusinessDayCalendar.Cairo.IsInvalidTime(candidate))
                        return date;
                }
            }
        }

        // DST is defined for Cairo; if the lookup ever fails, fall back to a
        // mid-April date so the assertion still exercises a real instant.
        return new DateOnly(year, 4, 15);
    }

    private static DateOnly OctoberTransitionDate(int year)
    {
        for (int day = 1; day <= 31; day++)
        {
            DateTime candidate = new(year, 10, day, 1, 0, 0, DateTimeKind.Unspecified);
            if (BusinessDayCalendar.Cairo.IsAmbiguousTime(candidate))
                return new DateOnly(year, 10, day);
        }

        return new DateOnly(year, 10, 25);
    }
}
