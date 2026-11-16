$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $changes = git status --porcelain
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Package only a clean, committed checkout.' }
    $commit = git rev-parse HEAD
    [xml]$project = Get-Content -LiteralPath 'LanDrop/LanDrop.csproj'
    $version = $project.Project.PropertyGroup.Version
    $name = "LanDrop-$version-win-x64"
    $output = Join-Path $root "artifacts/$commit"
    if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
    $payload = Join-Path $output $name
    New-Item -ItemType Directory -Path $payload | Out-Null
    dotnet publish LanDrop/LanDrop.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $payload
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath 'LICENSE' -Destination $payload
    Copy-Item -LiteralPath 'licenses' -Destination $payload -Recurse
    Copy-Item -LiteralPath 'docs/release-verification.md' -Destination $payload
    $sdkRoot = Split-Path -Parent (Get-Command dotnet).Source
    foreach ($notice in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
        Copy-Item -LiteralPath (Join-Path $sdkRoot $notice) -Destination (Join-Path $payload "dotnet-$notice")
    }
    $dependencies = dotnet list LanDrop/LanDrop.csproj package --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw 'Dependency inventory failed.' }
    $dependencies | Set-Content -LiteralPath (Join-Path $payload 'dependencies.json') -Encoding utf8
    $framework = $project.Project.PropertyGroup.TargetFramework
    $resolved = Get-Content -LiteralPath "LanDrop/bin/Release/$framework/win-x64/LanDrop.deps.json" -Raw | ConvertFrom-Json
    $runtimePacks = @($resolved.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.*.Runtime.win-x64/*' })
    if (-not ($runtimePacks -like '*Microsoft.NETCore.App.Runtime.win-x64/10.0.*') -or
        -not ($runtimePacks -like '*Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.*')) {
        throw 'Could not identify both resolved .NET 10 runtime packs.'
    }
    $provenance = [ordered]@{
        repository = 'https://github.com/sachiyacodes/LanDrop'
        commit = $commit
        version = $version
        sdk = (dotnet --version)
        hostRuntimes = @(dotnet --list-runtimes)
        bundledRuntimePacks = $runtimePacks
        target = 'win-x64'
        selfContained = $true
        signed = $false
        hardwareTests = 'Two-PC transfer and WiFi Direct checks pending; prerelease only.'
    }
    $provenance | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'provenance.json') -Encoding utf8
    $archive = Join-Path $output "$name.zip"
    Compress-Archive -Path "$payload/*" -DestinationPath $archive
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name.zip" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "Packaged $archive at $commit"
} finally {
    Pop-Location
}
