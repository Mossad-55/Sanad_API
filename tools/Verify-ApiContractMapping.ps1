[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')).Path
}

function Normalize-Route([string]$Path) {
    $normalized = $Path.TrimStart('/')
    return [regex]::Replace($normalized, '\{\{[^}]+\}\}|\{[^}]+\}', '{param}')
}

$controllerRoutes = [System.Collections.Generic.List[object]]::new()
$controllerRoot = Join-Path $RepositoryRoot 'src/API/Sanad.API/Controllers'

Get-ChildItem $controllerRoot -Filter '*.cs' -File | ForEach-Object {
    $source = Get-Content -Raw $_.FullName
    $routeAttribute = [regex]::Match($source, '\[Route\("([^"]+)"\)\]')
    if (-not $routeAttribute.Success) { return }

    $baseRoute = $routeAttribute.Groups[1].Value.Trim('/')
    foreach ($httpAttribute in [regex]::Matches(
        $source,
        '\[Http(Get|Post|Put|Patch|Delete)(?:\("([^"]*)"\))?\]')) {
        $suffix = $httpAttribute.Groups[2].Value
        $route = if ([string]::IsNullOrWhiteSpace($suffix)) {
            $baseRoute
        } else {
            "$baseRoute/$($suffix.TrimStart('/'))"
        }

        $controllerRoutes.Add([pscustomobject]@{
            Method = $httpAttribute.Groups[1].Value.ToUpperInvariant()
            Route = Normalize-Route $route
            Source = $_.FullName.Substring($RepositoryRoot.Length + 1)
        })
    }
}

$postmanRoutes = [System.Collections.Generic.List[object]]::new()
function Read-PostmanItems($Items, [string]$Collection) {
    foreach ($item in @($Items)) {
        if ($item.request -and $item.request.url) {
            $rawUrl = if ($item.request.url -is [string]) {
                $item.request.url
            } else {
                $item.request.url.raw
            }

            if ($rawUrl -match '/api/v1/') {
                $route = $rawUrl -replace '^.*?(/api/v1/)', '/api/v1/'
                $route = $route.Split('?')[0].TrimEnd('/')
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

$missing = @($controllerRoutes | Where-Object {
    $route = $_
    -not ($postmanRoutes | Where-Object {
        $_.Method -eq $route.Method -and $_.Route -eq $route.Route
    })
})
$orphan = @($postmanRoutes | Where-Object {
    $request = $_
    -not ($controllerRoutes | Where-Object {
        $_.Method -eq $request.Method -and $_.Route -eq $request.Route
    })
})

Write-Output "Controller actions: $($controllerRoutes.Count)"
Write-Output "Postman API requests: $($postmanRoutes.Count)"
Write-Output "Missing Postman mappings: $($missing.Count)"
Write-Output "Orphan Postman routes: $($orphan.Count)"

foreach ($item in $missing) { Write-Output "MISSING $($item.Method) /$($item.Route) [$($item.Source)]" }
foreach ($item in $orphan) { Write-Output "ORPHAN $($item.Method) /$($item.Route) [$($item.Collection)]" }

if ($missing.Count -gt 0 -or $orphan.Count -gt 0) { exit 1 }
