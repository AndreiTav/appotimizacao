param([switch]$SkipSmokeTest)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    function Invoke-Dotnet {
        & dotnet @args
        if ($LASTEXITCODE -ne 0) { throw "dotnet falhou com código $LASTEXITCODE" }
    }
    Invoke-Dotnet restore NexusOptimizer.sln --locked-mode
    Invoke-Dotnet build NexusOptimizer.sln -c Release --no-restore '-m:1'
    Invoke-Dotnet test tests/NexusOptimizer.Tests -c Release --no-build --no-restore --logger 'trx;LogFileName=core-tests.trx' --results-directory artifacts/test-results
    Invoke-Dotnet test tests/NexusOptimizer.Windows.Tests -c Release --no-build --no-restore --logger 'trx;LogFileName=windows-tests.trx' --results-directory artifacts/test-results
    Invoke-Dotnet publish src/NexusOptimizer.App -c Release -r win-x64 --self-contained true --no-restore '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' '-m:1' -o artifacts/win-x64
    Copy-Item README.md artifacts/win-x64/LEIA-ME.md -Force
    if (-not $SkipSmokeTest) { & "$PSScriptRoot/smoke-test.ps1" }
    $project = [xml](Get-Content -Raw src/NexusOptimizer.App/NexusOptimizer.App.csproj)
    $version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    $zip = Join-Path $root "artifacts/NEXUS-OPTIMIZER-$version-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path artifacts/win-x64/* -DestinationPath $zip
    (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $zip) | Set-Content "$zip.sha256" -Encoding ascii
    Write-Host "Pacote portátil: $zip"
} finally { Pop-Location }
