[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

Push-Location $repositoryRoot
try {
    git config --local core.hooksPath .githooks
    if ($LASTEXITCODE -ne 0) {
        throw 'No se pudo configurar core.hooksPath.'
    }

    Write-Host 'Hooks instalados: pre-commit y pre-push usarán scripts/validate-staged.ps1.' -ForegroundColor Green
}
finally {
    Pop-Location
}
