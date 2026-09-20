using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Families.Application.Reports;

public static class VisitReportErrors
{
    public static readonly Error AlreadySubmitted =
        new("Reports.Visit.AlreadySubmitted", "A visit report was already submitted for this booking.");

    public static readonly Error InvalidContent =
        new("Reports.Visit.InvalidContent", "The visit report content is invalid.");

    public static readonly Error InvalidType =
        new("Reports.Visit.InvalidType", "Only visit reports are supported.");

    public static readonly Error AccessDenied =
        new("Reports.Visit.AccessDenied", "The current user has no active family membership.");
}
