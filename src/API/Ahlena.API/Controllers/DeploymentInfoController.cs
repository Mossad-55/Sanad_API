using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ahlena.API.Controllers;

/// <summary>
/// Safe remote release evidence: package revision plus required vs.
/// actually applied migration identifiers for all nine active module contexts.
/// Anonymous by design (revision and migration IDs are not secrets);
/// carries no connection strings, tokens, or payloads.
/// </summary>
[Route("api/v1")]
public sealed class DeploymentInfoController(
    Deployment.IDeploymentManifestReader manifestReader,
    Deployment.IDeploymentMigrationProbe migrationProbe,
    IHostEnvironment environment) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpGet("deployment/info")]
    [ProducesResponseType(
        typeof(Deployment.DeploymentInfoResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeploymentInfo(
        CancellationToken cancellationToken)
    {
        Deployment.DeploymentManifestInfo manifest = manifestReader.Read();

        (bool databaseReachable, IReadOnlyDictionary<string, string?> applied) =
            await migrationProbe.GetAppliedAsync(cancellationToken);

        return Ok(new Deployment.DeploymentInfoResponse(
            "Ahlena.API",
            environment.EnvironmentName,
            manifest.Revision,
            manifest.RequiredMigrations,
            applied,
            databaseReachable));
    }
}
