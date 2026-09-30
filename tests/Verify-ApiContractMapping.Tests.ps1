[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$checkerPath = Join-Path $repositoryRoot 'tools/Verify-ApiContractMapping.ps1'
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

$requiredFunctions = @('Normalize-Route', 'Join-Route', 'Get-MissingRoutes', 'Get-OrphanRoutes')
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

$expectedRoutes = @(
    [pscustomobject]@{ Method = 'GET'; Route = 'api/v1/members/{param}' }
    [pscustomobject]@{ Method = 'POST'; Route = 'api/v1/members' }
)
$actualRoutes = @(
    [pscustomobject]@{ Method = 'GET'; Route = 'api/v1/members/{param}' }
    [pscustomobject]@{ Method = 'DELETE'; Route = 'api/v1/members/{param}' }
)

$missing = @(Get-MissingRoutes $expectedRoutes $actualRoutes)
$orphan = @(Get-OrphanRoutes $actualRoutes $expectedRoutes)
Assert-Equal 'one missing route is reported' 1 $missing.Count
Assert-Equal 'missing route details' 'POST api/v1/members' "$($missing[0].Method) $($missing[0].Route)"
Assert-Equal 'one orphan route is reported' 1 $orphan.Count
Assert-Equal 'orphan route details' 'DELETE api/v1/members/{param}' "$($orphan[0].Method) $($orphan[0].Route)"

Write-Output 'Verify-ApiContractMapping focused tests passed.'
