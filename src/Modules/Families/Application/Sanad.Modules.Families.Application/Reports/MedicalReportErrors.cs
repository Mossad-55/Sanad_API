using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Families.Application.Reports;

public static class MedicalReportErrors
{
    public static readonly Error InvalidContent = new("Reports.Medical.InvalidContent", "The medical report content is invalid.");
    public static readonly Error AlreadySubmitted = new("Reports.Medical.AlreadySubmitted", "A medical report was already submitted for this booking.");
    public static readonly Error CaregiverForbidden = new("Reports.Medical.CaregiverForbidden", "Only medical caregivers may submit medical reports.");
    public static readonly Error PhotoNotFound = new("Reports.Medical.PhotoNotFound", "The medical report photo was not found.");
    public static readonly Error AccessDenied = new("Reports.Medical.AccessDenied", "The current user cannot access this medical report.");
}
