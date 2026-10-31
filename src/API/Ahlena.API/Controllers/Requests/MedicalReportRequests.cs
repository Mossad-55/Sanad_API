using Ahlena.Modules.Families.Domain.Reports;

namespace Ahlena.API.Controllers.Requests;

public sealed record SubmitMedicalReportRequest(
    int? Systolic, int? Diastolic, int? Pulse, decimal? Temperature, string? Notes,
    DateTime? MeasurementTakenOnUtc, VisitReportAssessment Assessment,
    bool PhotoConsentConfirmed);
