[CmdletBinding()]
param(
    [switch]$AllowEmptyStagedDiff
)

$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$Command,

        [Parameter(ValueFromRemainingArguments)]
        [string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "El comando '$Command $($Arguments -join ' ')' terminó con código $LASTEXITCODE."
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$backendRoot = Join-Path $repositoryRoot 'backend'

Push-Location $repositoryRoot
try {
    git diff --cached --quiet --exit-code
    $diffExitCode = $LASTEXITCODE
    if ($diffExitCode -gt 1) {
        throw 'No se pudo leer el diff preparado.'
    }

    if ($diffExitCode -eq 0) {
        if (-not $AllowEmptyStagedDiff) {
            throw 'No hay cambios preparados. Usa git add y vuelve a ejecutar la validación.'
        }

        Write-Host 'No hay diff preparado; se validará el árbol de trabajo antes del push.'
    }
    else {
        Write-Host 'Diff preparado:'
        Invoke-Checked git diff --cached --stat
        Invoke-Checked git diff --cached --check
    }

    Push-Location $backendRoot
    try {
        Invoke-Checked dotnet build DeliveryBoard.slnx --nologo
        Invoke-Checked dotnet test DeliveryBoard.slnx --no-build --nologo
    }
    finally {
        Pop-Location
    }

    Write-Host 'Validación completada correctamente.' -ForegroundColor Green
}
finally {
    Pop-Location
}
