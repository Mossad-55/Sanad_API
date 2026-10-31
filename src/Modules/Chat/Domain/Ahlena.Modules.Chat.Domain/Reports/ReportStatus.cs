namespace Ahlena.Modules.Chat.Domain.Reports;

/// <summary>
/// Lifecycle of a chat report: opens on submission and is resolved once to
/// either Dismissed (history retained) or Upheld.
/// </summary>
public enum ReportStatus
{
    Open = 0,
    Dismissed = 1,
    Upheld = 2
}
