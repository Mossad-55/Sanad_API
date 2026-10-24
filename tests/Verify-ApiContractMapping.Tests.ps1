[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$checkerPath = Join-Path $repositoryRoot 'docs/internal/tools/Verify-ApiContractMapping.ps1'
$tokens = $null
$parseErrors = $null
$checkerAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $checkerPath,
    [ref]$tokens,
    [ref]$parseErrors
)

if ($parseErrors.Count -gt 0) {
    throw "Could not parse checker: $($parseErrors[0].Message)"
}

$requiredFunctions = @('Normalize-Route', 'Join-Route', 'Get-AttributeArguments')
$functionDefinitions = @($checkerAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $requiredFunctions -contains $node.Name
}, $true))

$foundNames = @($functionDefinitions | ForEach-Object Name | Sort-Object -Unique)
foreach ($requiredFunction in $requiredFunctions) {
    if ($requiredFunction -notin $foundNames) {
        throw "Checker function was not found: $requiredFunction"
    }
}

foreach ($definition in $functionDefinitions) {
    . ([scriptblock]::Create($definition.Extent.Text))
}

function Assert-Equal([string]$Name, $Expected, $Actual) {
    if ($Expected -cne $Actual) {
        throw "$Name failed. Expected '$Expected'; got '$Actual'."
    }
}

Assert-Equal 'class prefix and action route' 'api/v1/families/{param}/members' `
    (Normalize-Route (Join-Route 'api/v1/families/{familyId:int}' 'members'))
Assert-Equal 'absolute action route replaces class prefix' 'api/v1/members' `
    (Join-Route 'api/v1/families' '/api/v1/members')
Assert-Equal 'query string removal' 'api/v1/members' `
    (Normalize-Route '/api/v1/members?include=profile')
$brunoUrl = '{{baseUrl}}/api/v1/families/{{familyId}}?include=members'
$brunoApiRoute = [regex]::Match($brunoUrl, '(?i)/api/v1(?:/[^?#]*)?').Value
Assert-Equal 'Bruno baseUrl removal and variable normalization' 'api/v1/families/{param}' `
    (Normalize-Route $brunoApiRoute)
$apiRoutePattern = '(?i)(?:^|/)(api/v1)(?=/|$)(?:/[^?#]*)?'
$apiV10UrlPath = ('/api/v10/members' -split '[?#]', 2)[0]
Assert-Equal 'API v10 is not treated as API v1' $false `
    ([regex]::Match($apiV10UrlPath, $apiRoutePattern).Success)
$queryOnlyApiRoute = ('{{baseUrl}}/health?redirect=/api/v1/members' -split '[?#]', 2)[0]
Assert-Equal 'API-looking query value is ignored' $false `
    ([regex]::Match($queryOnlyApiRoute, $apiRoutePattern).Success)
Assert-Equal 'route constraint and variable normalization' 'api/v1/members/{param}' `
    (Normalize-Route '/api/v1/members/{id:guid}')
Assert-Equal 'numeric sample ID normalization' 'api/v1/members/{param}' `
    (Normalize-Route '/api/v1/members/4821')
Assert-Equal 'GUID sample ID normalization' 'api/v1/members/{param}' `
    (Normalize-Route '/api/v1/members/5a8c2301-3074-4fd0-92dc-83d35582378a')

$routeArguments = @(Get-AttributeArguments '[Route("api/v1/families")]' 'Route')
Assert-Equal 'controller route attribute extraction' 'api/v1/families' $routeArguments[0]

# --------------------------------------------------------------------------
# Restored failure-detection coverage: the checker's missing/orphan gate is
# exercised end-to-end by running the ACTUAL script against synthetic fixture
# repositories. The earlier local checks had deleted these assertions because
# the checker no longer exposes Get-MissingRoutes/Get-OrphanRoutes functions;
# running the real script in a child process restores the guarantee that the
# gate fails (exit 1) when a controller route lacks a Postman mapping or a
# Postman request resolves to no controller route, and passes only when the
# sets match. Fixture repos live in the OS temp directory and are removed
# afterwards; no repository file and no network is touched.
# --------------------------------------------------------------------------

function New-ApiMappingFixture {
    <# Builds a minimal repository root with one controller and one Postman
       collection in one of three scenarios:
       - Missing: controller documents GET+POST, collection documents GET+DELETE
         (POST is the missing mapping, DELETE the orphan Postman route).
       - Clean: controller and collection both document exactly GET+POST.
       - Negative: like Clean plus an extra item named '[no-route] ...' for a
         DELETE route that must not exist and must not fail the gate. #>
    param(
        [string]$Root,
        [ValidateSet('Missing', 'Clean', 'Negative')]
        [string]$Scenario
    )

    $controllerDir = Join-Path $Root 'src/API/Sanad.API/Controllers'
    $postmanDir = Join-Path $Root 'docs/postman'
    New-Item -ItemType Directory -Path $controllerDir -Force | Out-Null
    New-Item -ItemType Directory -Path $postmanDir -Force | Out-Null

    $controllerText = @'
using Microsoft.AspNetCore.Mvc;

namespace Sanad.API.Controllers;

[ApiController]
[Route("api/v1/members")]
public sealed class MembersController : ControllerBase
{
    [HttpGet("{id:guid}")]
    public IActionResult Get(Guid id) => Ok();

    [HttpPost]
    public IActionResult Create() => Ok();
}
'@

    $cleanCollectionText = @'
{
  "info": { "name": "Members", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
  "item": [
    {
      "name": "Get member",
      "request": {
        "method": "GET",
        "url": { "raw": "{{baseUrl}}/api/v1/members/5a8c2301-3074-4fd0-92dc-83d35582378a", "host": ["{{baseUrl}}"], "path": ["api", "v1", "members"] }
      }
    },
    {
      "name": "Create member",
      "request": {
        "method": "POST",
        "url": { "raw": "{{baseUrl}}/api/v1/members", "host": ["{{baseUrl}}"], "path": ["api", "v1", "members"] }
      }
    }
  ]
}
'@

    $missingCollectionText = @'
{
  "info": { "name": "Members", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
  "item": [
    {
      "name": "Get member",
      "request": {
        "method": "GET",
        "url": { "raw": "{{baseUrl}}/api/v1/members/5a8c2301-3074-4fd0-92dc-83d35582378a", "host": ["{{baseUrl}}"], "path": ["api", "v1", "members"] }
      }
    },
    {
      "name": "Delete member",
      "request": {
        "method": "DELETE",
        "url": { "raw": "{{baseUrl}}/api/v1/members/5a8c2301-3074-4fd0-92dc-83d35582378a", "host": ["{{baseUrl}}"], "path": ["api", "v1", "members"] }
      }
    }
  ]
}
'@

    $negativeCollectionText = @'
{
  "info": { "name": "Members", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
  "item": [
    {
      "name": "Get member",
      "request": {
        "method": "GET",
        "url": { "raw": "{{baseUrl}}/api/v1/members/5a8c2301-3074-4fd0-92dc-83d35582378a", "host": ["{{baseUrl}}"], "path": ["api", "v1", "members"] }
      }
    },
    {
      "name": "Create member",
      "request": {
        "method": "POST",
        "url": { "raw": "{{baseUrl}}/api/v1/members", "host": ["{{baseUrl}}"], "path": ["api", "v1", "members"] }
      }
    },
    {
      "name": "[no-route] Delete member",
      "request": {
        "method": "DELETE",
        "url": { "raw": "{{baseUrl}}/api/v1/members/5a8c2301-3074-4fd0-92dc-83d35582378a", "host": ["{{baseUrl}}"], "path": ["api", "v1", "members"] }
      }
    }
  ]
}
'@

    $collectionText = switch ($Scenario) {
        'Missing' { $missingCollectionText }
        'Clean' { $cleanCollectionText }
        'Negative' { $negativeCollectionText }
    }

    Set-Content -Path (Join-Path $controllerDir 'MembersController.cs') -Value $controllerText -Encoding UTF8
    Set-Content -Path (Join-Path $postmanDir 'Members.postman_collection.json') -Value $collectionText -Encoding UTF8
}

function Invoke-ApiMappingChecker {
    param([string]$FixtureRoot)

    $hostExecutable = (Get-Process -Id $PID).Path
    $output = & $hostExecutable -NoProfile -ExecutionPolicy Bypass -File $checkerPath -RepositoryRoot $FixtureRoot
    return @{
        ExitCode = $LASTEXITCODE
        Lines    = @($output | ForEach-Object { [string]$_ })
    }
}

$fixtureRoots = @()
try {
    # 1. Missing + orphan: the gate must fail with both diagnostics.
    $missingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-mapping-missing-' + [Guid]::NewGuid().ToString('N'))
    $fixtureRoots += $missingRoot
    New-ApiMappingFixture -Root $missingRoot -Scenario 'Missing'
    $missingRun = Invoke-ApiMappingChecker -FixtureRoot $missingRoot
    Assert-Equal 'missing-orphan fixture exits 1' 1 $missingRun.ExitCode
    $missingText = [string]::Join([Environment]::NewLine, $missingRun.Lines)
    foreach ($expectedLine in @(
        'Missing Postman mappings: 1',
        'MISSING POSTMAN POST /api/v1/members',
        'Orphan Postman routes: 1',
        'ORPHAN POSTMAN DELETE /api/v1/members/{param}')) {
        if (-not $missingText.Contains($expectedLine)) {
            throw "missing-orphan fixture did not report '$expectedLine'."
        }
    }

    # 2. Complete sets: the gate must pass with zero missing/orphan routes.
    $cleanRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-mapping-clean-' + [Guid]::NewGuid().ToString('N'))
    $fixtureRoots += $cleanRoot
    New-ApiMappingFixture -Root $cleanRoot -Scenario 'Clean'
    $cleanRun = Invoke-ApiMappingChecker -FixtureRoot $cleanRoot
    Assert-Equal 'clean fixture exits 0' 0 $cleanRun.ExitCode
    $cleanText = [string]::Join([Environment]::NewLine, $cleanRun.Lines)
    foreach ($expectedLine in @(
        'Missing Postman mappings: 0',
        'Orphan Postman routes: 0')) {
        if (-not $cleanText.Contains($expectedLine)) {
            throw "clean fixture did not report '$expectedLine'."
        }
    }

    # 3. Negative route documentation: an intentionally non-existent route
    #    named '[no-route]' must neither orphan-fail nor mask a real mapping.
    $negativeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-mapping-negative-' + [Guid]::NewGuid().ToString('N'))
    $fixtureRoots += $negativeRoot
    New-ApiMappingFixture -Root $negativeRoot -Scenario 'Negative'
    $negativeRun = Invoke-ApiMappingChecker -FixtureRoot $negativeRoot
    Assert-Equal 'negative-route fixture exits 0' 0 $negativeRun.ExitCode
    $negativeText = [string]::Join([Environment]::NewLine, $negativeRun.Lines)
    foreach ($expectedLine in @(
        'Missing Postman mappings: 0',
        'Orphan Postman routes: 0')) {
        if (-not $negativeText.Contains($expectedLine)) {
            throw "negative-route fixture did not report '$expectedLine'."
        }
    }
}
finally {
    foreach ($fixtureRoot in $fixtureRoots) {
        if ($fixtureRoot -and (Test-Path -LiteralPath $fixtureRoot)) {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }
}

Write-Output 'Verify-ApiContractMapping focused tests passed.'
