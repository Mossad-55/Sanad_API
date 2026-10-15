using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Sanad.GeographyImport;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine("Sanad.GeographyImport preview --catalogue FILE --report NEW_FILE");
            Console.WriteLine("Sanad.GeographyImport apply --catalogue FILE --plan FILE --plan-sha SHA256 --confirm-target FINGERPRINT --backup-reference LABEL --maintenance-reference LABEL");
            Console.WriteLine("Connection: explicit SANAD_GEOGRAPHY_CONNECTION environment variable only. No automatic API/startup integration.");
            Console.WriteLine("Optional exact owner-approved links: --reconciliation sanad-vps-v1 (same option required for preview/apply).");
            return 0;
        }
        try
        {
            var (mode, options) = ParseArguments(args);
            var catalogueBytes = await ReadBoundedAsync(options["--catalogue"], 2_000_000);
            var catalogue = ImportCatalogue.Parse(catalogueBytes, ImportCatalogue.AcceptedV1Sha256);
            if (catalogue.Rows.Count(row => row.Level == LocationLevel.Governorate) != 27 ||
                catalogue.Rows.Count(row => row.Level == LocationLevel.City) != 347 ||
                catalogue.Rows.Count(row => row.Level == LocationLevel.Area) != 5712)
                throw new ImportRejectedException("V1Counts");
            ImportPlan? reviewed = null;
            if (mode == "apply")
            {
                var planBytes = await ReadBoundedAsync(options["--plan"], 8_000_000);
                if (ImportJson.Hash(planBytes) != options["--plan-sha"].ToUpperInvariant()) throw new ImportRejectedException("ReviewedPlanHash");
                reviewed = JsonSerializer.Deserialize<ImportPlan>(planBytes, ImportJson.Options)
                    ?? throw new ImportRejectedException("PlanNull");
            }
            else if (File.Exists(options["--report"])) throw new ImportRejectedException("ReportAlreadyExists");

            var connection = Environment.GetEnvironmentVariable("SANAD_GEOGRAPHY_CONNECTION");
            if (string.IsNullOrWhiteSpace(connection)) throw new ImportRejectedException("ExplicitConnectionRequired");
            var builder = new NpgsqlConnectionStringBuilder(connection);
            if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database))
                throw new ImportRejectedException("ExplicitTargetRequired");
            var target = ImportJson.Hash(Encoding.UTF8.GetBytes($"{builder.Host}\n{builder.Port}\n{builder.Database}"));
            builder.IncludeErrorDetail = false;
            builder.PersistSecurityInfo = false;
            builder.Pooling = false;
            await using var db = new LocationDbContext(new DbContextOptionsBuilder<LocationDbContext>()
                .UseNpgsql(builder.ConnectionString, provider => provider.CommandTimeout(30)).Options);
            var store = new ImportStore(db, target, useApprovedVpsLinks: options.ContainsKey("--reconciliation"));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            if (mode == "preview")
            {
                var plan = await store.PreviewAsync(catalogue, cancellation.Token);
                var bytes = ImportJson.Bytes(plan);
                await using var output = new FileStream(options["--report"], FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await output.WriteAsync(bytes, cancellation.Token);
                await output.FlushAsync(cancellation.Token);
                Console.WriteLine($"Preview only. Inserts={plan.InsertCount}; conflicts={plan.Rows.Count(row => row.Action == ImportAction.Conflict)}; preserved extras={plan.PreservedExtras.Count}.");
                Console.WriteLine($"Target fingerprint: {target}");
                Console.WriteLine($"Reviewed plan SHA-256: {ImportJson.Hash(bytes)}");
                return plan.HasConflicts ? 2 : 0;
            }
            var inserted = await store.ApplyAsync(catalogue, reviewed!, options["--confirm-target"], options["--backup-reference"], options["--maintenance-reference"], cancellation.Token);
            Console.WriteLine($"Transaction committed. Inserted={inserted}. Existing rows preserved. Retain reviewed plan and backup reference in the operator record.");
            return 0;
        }
        catch (ImportRejectedException exception)
        {
            Console.Error.WriteLine($"Import refused: {exception.Message}. Review the target/plan; no automatic repair or retry.");
            return 2;
        }
        catch (Exception)
        {
            // Never print provider exceptions/connection strings/credentials.
            Console.Error.WriteLine("Operation failed or was cancelled. Verify database state before retrying; retain the preview and inspect configuration securely. No automatic retry.");
            return 1;
        }
    }

    public static (string Mode, Dictionary<string, string> Options) ParseArguments(string[] args)
    {
        if (args.Length < 1 || args[0] is not ("preview" or "apply") || args.Length % 2 != 1)
            throw new ImportRejectedException("Arguments");
        var required = args[0] == "preview" ? new[] { "--catalogue", "--report" } :
            ["--catalogue", "--plan", "--plan-sha", "--confirm-target", "--backup-reference", "--maintenance-reference"];
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
            if ((!required.Contains(args[index], StringComparer.Ordinal) && args[index] != "--reconciliation") || string.IsNullOrWhiteSpace(args[index + 1]) ||
                !options.TryAdd(args[index], args[index + 1])) throw new ImportRejectedException("Arguments");
        if (required.Any(key => !options.ContainsKey(key))) throw new ImportRejectedException("Arguments");
        if (options.TryGetValue("--reconciliation", out var profile) && profile != ApprovedVpsLinks.Profile)
            throw new ImportRejectedException("ReconciliationProfile");
        return (args[0], options);
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximum)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is 0 || stream.Length > maximum) throw new ImportRejectedException("InputSize");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes);
        return bytes;
    }
}
