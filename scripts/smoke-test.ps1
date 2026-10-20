$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'artifacts/win-x64/NEXUS OPTIMIZER.exe'
$result = Join-Path $root 'artifacts/test-results/windows-ui-smoke.json'
New-Item -ItemType Directory -Path (Split-Path -Parent $result) -Force | Out-Null
if (Test-Path $result) { Remove-Item $result }
$previous = $env:NEXUS_SMOKE_RESULT
try {
    $env:NEXUS_SMOKE_RESULT = $result
    $process = Start-Process -FilePath $exe -ArgumentList '--smoke-test' -PassThru
    if (-not $process.WaitForExit(90000)) {
        $process.Kill()
        throw 'O smoke test excedeu 90 segundos.'
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) {
        if (Test-Path $result) { Write-Host (Get-Content -Raw $result) }
        throw "O aplicativo retornou código $($process.ExitCode). Resultado: $result"
    }
    if (-not (Test-Path $result)) { throw 'O aplicativo não produziu o resultado do smoke test.' }
    $data = Get-Content -Raw $result | ConvertFrom-Json
    if (-not $data.Passed -or $data.Pages -ne 9) { throw 'Falha de inicialização ou carregamento dos módulos.' }
    Write-Host 'Smoke test: janela exibida, 9 páginas carregadas, memória e planos detectados; nenhum ajuste aplicado.'
} finally { $env:NEXUS_SMOKE_RESULT = $previous }
