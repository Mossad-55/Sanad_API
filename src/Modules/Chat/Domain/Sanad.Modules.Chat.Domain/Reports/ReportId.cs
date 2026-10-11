namespace Sanad.Modules.Chat.Domain.Reports;

public readonly record struct ReportId(Guid Value)
{
    public static ReportId New() => new(Guid.CreateVersion7());
    public static ReportId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
