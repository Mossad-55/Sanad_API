using System;
using Sanad.BuildingBlocks.Application.Abstractions;

namespace Sanad.UnitTests.CareHomes;

/// <summary>
/// Shared care-home test clock for handlers whose constructor now takes
/// IDateTimeProvider (2026-10-27 lifecycle work). Tests that assert the new
/// expiry boundaries construct their own fixed instance; these call-site
/// patches only need a stable IDateTimeProvider to compile and behave as
/// before for the untouched assertions.
/// </summary>
internal sealed class CareHomeTestClock(DateTime utcNow) : IDateTimeProvider
{
    public DateTime UtcNow { get; } = utcNow;
}

/// <summary>
/// Stable <see cref="IDateTimeProvider"/> accessor for call sites whose only
/// need is the handler's injected-clock constructor (2026-10-27 lifecycle
/// work). Returns a UtcNow-based clock so every patched call site keeps the
/// pre-change behavior exactly; tests that assert expiry BOUNDARIES build
/// their own fixed <see cref="CareHomeTestClock"/> instead.
/// </summary>
internal static class CareHomeTestClockHost
{
    public static CareHomeTestClock Clock() =>
        new(DateTime.UtcNow);
}
