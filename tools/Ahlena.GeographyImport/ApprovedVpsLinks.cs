namespace Ahlena.GeographyImport;

// Exact owner-approved operational links, never fuzzy matching or row updates.
public static class ApprovedVpsLinks
{
    public const string Profile = "ahlena-vps-v1";
    public const string Target = "32FB15696C9EA437CAB63FF014B6A56C1DCC737478A184367B0AEF02F1891020";
    public static IReadOnlyDictionary<string, ExistingLocation> Resolve(ImportCatalogue catalogue,
        IReadOnlyList<ExistingLocation> snapshot, string target)
    {
        if (target != Target || catalogue.Sha256 != ImportCatalogue.AcceptedV1Sha256)
            throw new ImportRejectedException("ReconciliationTarget");
        var definitions = new[]
        {
            ("EG18", "01a07c6a-6e2a-70cf-b63a-094d87a95777", "البحيرة", "Beheira", (string?)null),
            ("EG24", "01a07c7e-e825-7dfb-a7fb-5f7ce01d9845", "المنيا", "Minya", (string?)null),
            ("EG25", "01a07c28-9228-71f4-9717-ade72428ecf2", "اسيوط", "Asyut", (string?)null),
            ("EG1801", "01a07c6b-8664-7e17-b938-0ba33e950f75", "دمنهور", "Damanhur", "01a07c6a-6e2a-70cf-b63a-094d87a95777")
        };
        var result = new Dictionary<string, ExistingLocation>(StringComparer.Ordinal);
        foreach (var (key, id, arabic, english, parent) in definitions)
        {
            var source = catalogue.Rows.SingleOrDefault(row => row.Key == key);
            var match = snapshot.SingleOrDefault(row => row.Id == Guid.Parse(id));
            if (source is null || match is null || match.Level != source.Level ||
                match.ArabicName != arabic || match.EnglishName != english ||
                match.ParentId != (parent is null ? (Guid?)null : Guid.Parse(parent)))
                throw new ImportRejectedException("ReconciliationDrift");
            result.Add(key, match);
        }
        return result;
    }
}
