namespace Ahlena.PortalProvisioner;

/// <summary>
/// Parse-time validation of the CLI <c>--groups</c> list, before any plan,
/// connection or write. Extracted from the top-level program so the refusal
/// rules are unit-testable offline (the 2026-10-26 rehearsal incident: a
/// provision invocation whose group list ended up empty reached the portal
/// fixture path, whose base fixture ran and wrote rows while zero groups were
/// requested — the fail-closed group gate inside the provisioner can only
/// judge listed groups, so an empty list slipped through it).
/// </summary>
public static class ProvisionerCliGroups
{
    /// <summary>
    /// The parse-time refusals for the requested group list. A provision run
    /// must list at least one group: no group requested is indistinguishable
    /// from "provision the portal fixture base entities only", which is what
    /// silently wrote rows in the incident. A verify run never writes, so the
    /// real recorded verify shape (no --groups at all) stays valid. Every
    /// listed id — in any mode — must be a known group of exactly one corpus:
    /// a demo group or an S3a report-fixture group (data-only or seam-gated);
    /// a shell-expansion accident like <c>0</c> is refused here, at parse
    /// time, instead of deep inside a write path.
    /// </summary>
    public static IReadOnlyList<string> Refusals(IReadOnlyList<string> groupArgs, bool provisioning)
    {
        List<string> refusals = new();
        if (provisioning && groupArgs.Count == 0)
        {
            refusals.Add(
                "a provision run must list at least one group with --groups (repeat the flag per group). "
                    + "An empty or missing group list is refused because nothing requested is "
                    + "indistinguishable from 'provision the portal fixture base entities only'. "
                    + "Verify runs without --groups stay valid: they never write.");
        }

        foreach (string group in groupArgs)
        {
            bool demo = DemoCorpusGroups.IsDemoGroup(group);
            bool fixture = PortalProvisionGroups.DataOnlyGroups.Contains(group)
                || PortalProvisionGroups.SeamGroups.Contains(group);
            if (!demo && !fixture)
            {
                refusals.Add(
                    $"'{group}' is not a known group of either corpus (demo-corpus or S3a report-fixture); "
                        + "including a shell-expansion accident like a bare 0.");
            }
        }

        return refusals;
    }
}
