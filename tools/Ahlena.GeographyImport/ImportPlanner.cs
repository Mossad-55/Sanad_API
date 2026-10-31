namespace Ahlena.GeographyImport;

public static class ImportPlanner
{
    public static string Fingerprint(IReadOnlyList<ExistingLocation> snapshot) => ImportJson.Hash(ImportJson.Bytes(
        snapshot.OrderBy(row => row.Level).ThenBy(row => row.Id).ToArray()));

    public static ImportPlan Build(ImportCatalogue catalogue, IReadOnlyList<ExistingLocation> snapshot,
        string targetFingerprint, DateTime createdOnUtc, Func<SourceLocation, Guid>? newId = null,
        bool useApprovedVpsLinks = false)
    {
        catalogue = ImportCatalogue.Create(catalogue.Sha256, catalogue.Rows);
        var aliases = useApprovedVpsLinks ? ApprovedVpsLinks.Resolve(catalogue, snapshot, targetFingerprint) :
            new Dictionary<string, ExistingLocation>(StringComparer.Ordinal);
        if (createdOnUtc.Kind != DateTimeKind.Utc) throw new ImportRejectedException("PlanClock");
        // PostgreSQL timestamps have microsecond precision. Pin plan/write/read
        // timestamps to that precision so post-insert verification is portable.
        createdOnUtc = new DateTime(createdOnUtc.Ticks - createdOnUtc.Ticks % 10, DateTimeKind.Utc);
        var existing = new Dictionary<(LocationLevel, Guid), ExistingLocation>();
        foreach (var row in snapshot)
        {
            if (row.Id == Guid.Empty || !Enum.IsDefined(row.Level) || !existing.TryAdd((row.Level, row.Id), row))
                throw new ImportRejectedException("TargetDuplicateId");
        }
        foreach (var row in snapshot)
            if (row.Level == LocationLevel.Governorate ? row.ParentId is not null :
                row.ParentId is null || !existing.ContainsKey(((LocationLevel)((int)row.Level - 1), row.ParentId.Value)))
                throw new ImportRejectedException("TargetOrphan");

        var planned = new Dictionary<string, PlannedLocation>(StringComparer.Ordinal);
        var used = new HashSet<(LocationLevel, Guid)>();
        var allocated = snapshot.Select(row => row.Id).ToHashSet();
        foreach (var source in catalogue.Rows)
        {
            Guid? parentId = null;
            PlannedLocation? parent = null;
            if (source.ParentKey is not null)
            {
                parent = planned[source.ParentKey];
                parentId = parent.Id;
            }
            PlannedLocation Conflict(string code, ExistingLocation[]? candidates = null) => new(source.Level, source.Key, Guid.Empty, parentId,
                source.ArabicName, source.EnglishName, ImportAction.Conflict, false, code)
                { MatchingTargetIds = (candidates ?? []).Select(row => row.Id).Order().ToArray() };
            if (parent?.Action == ImportAction.Conflict) { planned.Add(source.Key, Conflict("ParentConflict")); continue; }
            var matches = snapshot.Where(row => row.Level == source.Level && row.ParentId == parentId &&
                (row.ArabicName == source.ArabicName || row.EnglishName == source.EnglishName)).ToArray();
            PlannedLocation result;
            if (aliases.TryGetValue(source.Key, out var approved))
            {
                if (approved.ParentId != parentId || matches.Any(row => row.Id != approved.Id) ||
                    !used.Add((approved.Level, approved.Id))) result = Conflict("ReconciliationCollision", matches);
                else result = new(source.Level, source.Key, approved.Id, parentId, approved.ArabicName, approved.EnglishName,
                    approved.IsActive ? ImportAction.Keep : ImportAction.KeepInactive, approved.IsActive, null);
            }
            else if (matches.Length > 1) result = Conflict("AmbiguousNames", matches);
            else if (matches.Length == 1)
            {
                var match = matches[0];
                if (match.ArabicName != source.ArabicName || match.EnglishName != source.EnglishName)
                    result = Conflict("PartialNameMatch", matches);
                else if (!used.Add((match.Level, match.Id))) result = Conflict("ReusedTargetId");
                else result = new(source.Level, source.Key, match.Id, parentId, match.ArabicName, match.EnglishName,
                    match.IsActive ? ImportAction.Keep : ImportAction.KeepInactive, match.IsActive, null);
            }
            else if (parent is { IsActive: false }) result = Conflict("InactiveParent");
            else
            {
                // Existing inactive ancestors can also sit above an active City.
                var ancestor = parent;
                while (ancestor is not null && ancestor.IsActive && catalogue.Rows.Single(row => row.Key == ancestor.SourceKey).ParentKey is { } key)
                    ancestor = planned[key];
                if (ancestor is { IsActive: false }) result = Conflict("InactiveAncestor");
                else
                {
                    var id = newId?.Invoke(source) ?? Guid.NewGuid();
                    if (id == Guid.Empty || !allocated.Add(id)) throw new ImportRejectedException("NewIdCollision");
                    result = new(source.Level, source.Key, id, parentId, source.ArabicName, source.EnglishName, ImportAction.Insert, true, null);
                }
            }
            planned.Add(source.Key, result);
        }
        return new(1, catalogue.Sha256, targetFingerprint, Fingerprint(snapshot), createdOnUtc,
            planned.Values.ToArray(), snapshot.Where(row => !used.Contains((row.Level, row.Id)))
                .OrderBy(row => row.Level).ThenBy(row => row.Id).ToArray())
            { Reconciliation = useApprovedVpsLinks ? ApprovedVpsLinks.Profile : null };
    }
}
