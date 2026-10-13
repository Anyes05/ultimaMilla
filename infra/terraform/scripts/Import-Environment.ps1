param(
    [string]$Path = (Join-Path $PSScriptRoot "..\.env")
)

$ErrorActionPreference = "Stop"

$sensitiveVariableNames = @(
    "TF_VAR_database_master_password"
)

if (-not (Test-Path -LiteralPath $Path)) {
    throw "No se encontro el archivo .env en $Path."
}

$loadedVariables = 0

foreach ($line in Get-Content -LiteralPath $Path) {
    $trimmedLine = $line.Trim()

    if ([string]::IsNullOrWhiteSpace($trimmedLine) -or $trimmedLine.StartsWith("#")) {
        continue
    }

    $separatorIndex = $trimmedLine.IndexOf("=")
    if ($separatorIndex -lt 1) {
        throw "La linea '$trimmedLine' no tiene el formato NOMBRE=VALOR."
    }

    $name = $trimmedLine.Substring(0, $separatorIndex).Trim()
    $value = $trimmedLine.Substring($separatorIndex + 1)

    if ($name -in $sensitiveVariableNames) {
        Write-Warning "$name se omite desde .env. Ingreselo durante el despliegue."
        continue
    }

    [Environment]::SetEnvironmentVariable($name, $value, "Process")
    $loadedVariables++
}

Write-Host "Se cargaron $loadedVariables variables de entorno desde .env."