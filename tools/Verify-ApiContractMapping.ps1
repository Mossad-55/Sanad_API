[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')).Path
}

function Normalize-Route([string]$Path) {
    $normalized = [uri]::UnescapeDataString(($Path -split '[?#]', 2)[0]).Trim('/')
    $normalized = [regex]::Replace($normalized, '\{\{[^{}]+\}\}|\{[^{}]+\}', '{param}')
    $normalized = [regex]::Replace($normalized, '(?i)(?<=/)([0-9]+|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(?=/|$)', '{param}')
    return [regex]::Replace($normalized, '/+', '/')
}

function Join-Route([string]$Base, [string]$Suffix) {
    if ([string]::IsNullOrWhiteSpace($Suffix)) { return $Base.Trim('/') }
    if ($Suffix.StartsWith('/')) { return $Suffix.Trim('/') }
    return (($Base.Trim('/') + '/' + $Suffix.Trim('/')).Trim('/'))
}

function Get-AttributeArguments([string]$Text, [string]$AttributeName) {
    $values = [System.Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches($Text, "\[$AttributeName(?:\(([^)]*)\))?\]")) {
        $value = $match.Groups[1].Value
        if ([string]::IsNullOrWhiteSpace($value)) { $values.Add('') }
        elseif ($value -match '^\s*"([^"]*)"') { $values.Add($Matches[1]) }
    }
    return ,$values.ToArray()
}

$controllerRoutes = [System.Collections.Generic.List[object]]::new()
$controllerRoot = Join-Path $RepositoryRoot 'src/API/Sanad.API/Controllers'

Get-ChildItem $controllerRoot -Filter '*.cs' -File | ForEach-Object {
    $sourceText = Get-Content -Raw $_.FullName
    $classMatch = [regex]::Match($sourceText, '(?s)((?:\s*\[[^\]]+\]\s*)+)\s*(?:public\s+)?(?:abstract\s+|sealed\s+)?(?:partial\s+)?class\s+\w+')
    if (-not $classMatch.Success) { return }
    $classRoutes = @(Get-AttributeArguments $classMatch.Groups[1].Value 'Route')
    if ($classRoutes.Count -eq 0) { return }

    $previousHttpEnd = $classMatch.Index + $classMatch.Length
    foreach ($httpAttribute in [regex]::Matches(
        $sourceText,
        '\[(HttpGet|HttpPost|HttpPut|HttpPatch|HttpDelete)(?:\("([^"]*)"\))?\]')) {
        $method = $httpAttribute.Groups[1].Value.Substring(4).ToUpperInvariant()
        $suffix = $httpAttribute.Groups[2].Value

        # An action-level [Route] is commonly placed before the HTTP verb attribute.
        $actionRoute = $null
        foreach ($routeMatch in [regex]::Matches($sourceText.Substring($previousHttpEnd, $httpAttribute.Index - $previousHttpEnd), '\[Route\("([^"]*)"\)\]')) {
            $actionRoute = $routeMatch.Groups[1].Value
        }
        if ($null -ne $actionRoute) { $suffix = $actionRoute }

        foreach ($baseRoute in $classRoutes) {
            $route = Join-Route $baseRoute $suffix
            $controllerRoutes.Add([pscustomobject]@{
                Method = $method
                Route = Normalize-Route $route
                Source = $_.FullName.Substring($RepositoryRoot.Length + 1)
            })
        }
        $previousHttpEnd = $httpAttribute.Index + $httpAttribute.Length
    }
}

$postmanRoutes = [System.Collections.Generic.List[object]]::new()
function Read-PostmanItems($Items, [string]$Collection) {
    foreach ($item in @($Items)) {
        if ($item.request -and $item.request.url) {
            $rawUrl = if ($item.request.url -is [string]) { $item.request.url } else { $item.request.url.raw }
            if ($rawUrl -match '/api/v1/') {
                $route = $rawUrl -replace '^.*?(/api/v1/)', '/api/v1/'
                $postmanRoutes.Add([pscustomobject]@{
                    Method = ([string]$item.request.method).ToUpperInvariant()
                    Route = Normalize-Route $route
                    Collection = $Collection
                })
            }
        }
        if ($item.item) { Read-PostmanItems $item.item $Collection }
    }
}

$postmanRoot = Join-Path $RepositoryRoot 'docs/postman'
Get-ChildItem $postmanRoot -Recurse -Filter '*collection.json' -File | ForEach-Object {
    $collection = Get-Content -Raw $_.FullName | ConvertFrom-Json
    Read-PostmanItems $collection.item $_.FullName.Substring($RepositoryRoot.Length + 1)
}

$brunoRoutes = [System.Collections.Generic.List[object]]::new()
$brunoRoot = Join-Path $RepositoryRoot 'tests/Bruno/collections'
Get-ChildItem $brunoRoot -Recurse -Filter '*.bru' -File | ForEach-Object {
    $requestText = Get-Content -Raw $_.FullName
    $methodMatch = [regex]::Match($requestText, '(?im)^\s*(get|post|put|patch|delete)\s*\{')
    if (-not $methodMatch.Success) { return }

    $urlMatch = [regex]::Match($requestText, '(?im)^\s*url:\s*(\S+)')
    if (-not $urlMatch.Success) { return }
    $rawUrl = $urlMatch.Groups[1].Value.Trim('"', "'")
    $rawPath = ($rawUrl -split '[?#]', 2)[0]
    $apiMatch = [regex]::Match($rawPath, '(?i)(?:^|/)(api/v1)(?=/|$)(?:/[^?#]*)?')
    if (-not $apiMatch.Success) { return }
    $routeStart = $apiMatch.Groups[1].Index - $apiMatch.Index
    $route = $apiMatch.Value.Substring($routeStart)

    $brunoRoutes.Add([pscustomobject]@{
        Method = $methodMatch.Groups[1].Value.ToUpperInvariant()
        Route = Normalize-Route $route
        Request = $_.FullName.Substring($RepositoryRoot.Length + 1)
    })
}

function Get-MissingRoutes($Expected, $Actual) {
    return @($Expected | Where-Object {
        $candidate = $_
        -not ($Actual | Where-Object { $_.Method -eq $candidate.Method -and $_.Route -eq $candidate.Route })
    })
}
function Get-OrphanRoutes($Actual, $Expected) {
    return @($Actual | Where-Object {
        $candidate = $_
        -not ($Expected | Where-Object { $_.Method -eq $candidate.Method -and $_.Route -eq $candidate.Route })
    })
}

$postmanMissing = @(Get-MissingRoutes $controllerRoutes $postmanRoutes)
$postmanOrphan = @(Get-OrphanRoutes $postmanRoutes $controllerRoutes)
$brunoMissing = @(Get-MissingRoutes $controllerRoutes $brunoRoutes)
$brunoOrphan = @(Get-OrphanRoutes $brunoRoutes $controllerRoutes)

Write-Output "Controller route-method signatures: $($controllerRoutes.Count)"
Write-Output "Postman API requests: $($postmanRoutes.Count)"
Write-Output "Missing Postman mappings: $($postmanMissing.Count)"
Write-Output "Orphan Postman routes: $($postmanOrphan.Count)"
Write-Output "Bruno API requests: $($brunoRoutes.Count)"
Write-Output "Missing Bruno mappings: $($brunoMissing.Count)"
Write-Output "Orphan Bruno routes: $($brunoOrphan.Count)"

foreach ($item in $postmanMissing) { Write-Output "MISSING POSTMAN $($item.Method) /$($item.Route) [$($item.Source)]" }
foreach ($item in $postmanOrphan) { Write-Output "ORPHAN POSTMAN $($item.Method) /$($item.Route) [$($item.Collection)]" }
foreach ($item in $brunoMissing) { Write-Output "MISSING BRUNO $($item.Method) /$($item.Route) [$($item.Source)]" }
foreach ($item in $brunoOrphan) { Write-Output "ORPHAN BRUNO $($item.Method) /$($item.Route) [$($item.Request)]" }

if ($postmanMissing.Count -gt 0 -or $postmanOrphan.Count -gt 0 -or $brunoMissing.Count -gt 0 -or $brunoOrphan.Count -gt 0) { exit 1 }
