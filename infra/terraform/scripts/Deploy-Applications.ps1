param(
    [ValidatePattern("^[0-9a-f]{40}$")]
    [string]$ImageTag,
    [string]$Namespace = "tallernet",
    [string]$JwtIssuer = "ultima-milla",
    [string]$JwtAudience = "ultima-milla",
    [switch]$EnableWorker
)

$ErrorActionPreference = "Stop"

$terraformDirectory = Split-Path -Parent $PSScriptRoot
Set-Location $terraformDirectory

if ([string]::IsNullOrWhiteSpace($ImageTag)) {
    $repositoryRoot = Split-Path -Parent (Split-Path -Parent $terraformDirectory)
    $ImageTag = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo obtener el SHA del commit actual. Indique -ImageTag con el SHA publicado por CI/CD."
    }

    Write-Host "Usando la imagen del commit actual: $ImageTag"
}

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name es requerido para desplegar las aplicaciones."
    }
}

function ConvertTo-PlainText {
    param([Parameter(Mandatory)][Security.SecureString]$Value)

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function ConvertTo-Base64 {
    param([Parameter(Mandatory)][string]$Value)

    return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Value))
}

function Get-SecretValueBase64 {
    # Valor (en base64) de una clave del secret si ya existe en el clúster; vacío si no existe el secret o la clave.
    param([Parameter(Mandatory)][string]$Key)

    $value = & kubectl -n $Namespace get secret tallernet-application-secrets -o "jsonpath={.data.$Key}" 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) { return "" }
    return $value.Trim()
}

function Invoke-KubectlApply {
    param([Parameter(Mandatory)][string]$Manifest)

    $Manifest | & kubectl apply -f -
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Assert-Command -Name "aws"
Assert-Command -Name "kubectl"
Assert-Command -Name "terraform"
Assert-Command -Name "git"

$clusterName = (& terraform output -raw eks_cluster_name).Trim()
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$awsRegion = (& aws configure get region).Trim()
if ([string]::IsNullOrWhiteSpace($awsRegion)) { $awsRegion = "us-east-1" }

& aws eks update-kubeconfig --region $awsRegion --name $clusterName
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$rdsEndpoint = (& terraform output -raw rds_endpoint).Trim()
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$repositories = & terraform output -json ecr_repository_urls | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$rdsPassword = ConvertTo-PlainText (Read-Host -Prompt "Contrasena maestra de PostgreSQL" -AsSecureString)
$jwtKey = ConvertTo-PlainText (Read-Host -Prompt "Clave JWT de produccion" -AsSecureString)
$avisoSigningKey = ConvertTo-PlainText (Read-Host -Prompt "Clave de firma HMAC para avisos" -AsSecureString)
$mongoConnectionStringBase64 = & kubectl -n $Namespace get secret tallernet-application-secrets `
    -o "jsonpath={.data.Mongo__ConnectionString}" 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($mongoConnectionStringBase64)) {
    $mongoConnectionStringBase64 = ""
}
if ([string]::IsNullOrWhiteSpace($mongoConnectionStringBase64)) {
    $mongoConnectionString = ConvertTo-PlainText (Read-Host -Prompt "Cadena de conexion MongoDB Atlas" -AsSecureString)
    $mongoConnectionStringBase64 = ConvertTo-Base64 $mongoConnectionString
}

# Claves opcionales que se cargan aparte (no van en el repositorio). Al volver a aplicar el secret se conservan las
# que ya existen; si el ambiente es nuevo se piden, y con Enter quedan sin configurar: el worker sigue funcionando y
# deja el push y los correos en su log en lugar de enviarlos.
$optionalSecretData = [ordered]@{}

# Push de reasignación urgente (letra 6.11 y opcional 7.3): cuenta de servicio de Firebase.
$firebaseBase64 = Get-SecretValueBase64 -Key "Push__Fcm__CuentaServicioJson"
if ([string]::IsNullOrWhiteSpace($firebaseBase64)) {
    $firebasePath = Read-Host -Prompt "Ruta del JSON de la cuenta de servicio de Firebase (Enter para omitir)"
    if (-not [string]::IsNullOrWhiteSpace($firebasePath)) {
        if (-not (Test-Path -LiteralPath $firebasePath)) { throw "No existe el archivo $firebasePath." }
        $firebaseBase64 = ConvertTo-Base64 (Get-Content -Raw -LiteralPath $firebasePath)
    }
}
if (-not [string]::IsNullOrWhiteSpace($firebaseBase64)) {
    $optionalSecretData["Push__Fcm__CuentaServicioJson"] = $firebaseBase64
}

# Correo al destinatario (RF 27): servidor SMTP.
$smtpKeys = @("Notificaciones__SmtpHost", "Notificaciones__SmtpUsuario", "Notificaciones__SmtpClave", "Notificaciones__Remitente")
$smtpExisting = [ordered]@{}
foreach ($smtpKey in $smtpKeys) { $smtpExisting[$smtpKey] = Get-SecretValueBase64 -Key $smtpKey }
if ([string]::IsNullOrWhiteSpace($smtpExisting["Notificaciones__SmtpHost"])) {
    $smtpHost = Read-Host -Prompt "Servidor SMTP para los correos al destinatario (Enter para omitir)"
    if (-not [string]::IsNullOrWhiteSpace($smtpHost)) {
        $smtpExisting["Notificaciones__SmtpHost"] = ConvertTo-Base64 $smtpHost
        $smtpExisting["Notificaciones__SmtpUsuario"] = ConvertTo-Base64 (Read-Host -Prompt "Usuario SMTP")
        $smtpExisting["Notificaciones__SmtpClave"] = ConvertTo-Base64 (ConvertTo-PlainText (Read-Host -Prompt "Clave SMTP" -AsSecureString))
        $smtpExisting["Notificaciones__Remitente"] = ConvertTo-Base64 (Read-Host -Prompt "Remitente de los correos")
    }
}
foreach ($smtpKey in $smtpKeys) {
    if (-not [string]::IsNullOrWhiteSpace($smtpExisting[$smtpKey])) {
        $optionalSecretData[$smtpKey] = $smtpExisting[$smtpKey]
    }
}

try {
    $connectionString = "Host=$rdsEndpoint;Port=5432;Database=tallernet;Username=tallernet_admin;Password=$rdsPassword;SSL Mode=Require"
    $secretManifest = [ordered]@{
        apiVersion = "v1"
        kind       = "Secret"
        metadata   = [ordered]@{
            name      = "tallernet-application-secrets"
            namespace = $Namespace
        }
        type       = "Opaque"
        data       = [ordered]@{
            ConnectionStrings__Default = ConvertTo-Base64 $connectionString
            Jwt__Clave                 = ConvertTo-Base64 $jwtKey
            Jwt__Emisor                = ConvertTo-Base64 $JwtIssuer
            Jwt__Audiencia             = ConvertTo-Base64 $JwtAudience
            Avisos__ClaveFirma         = ConvertTo-Base64 $avisoSigningKey
            Mongo__ConnectionString    = $mongoConnectionStringBase64
        }
    }
    foreach ($optionalKey in $optionalSecretData.Keys) {
        $secretManifest.data[$optionalKey] = $optionalSecretData[$optionalKey]
    }
    $secretManifest = $secretManifest | ConvertTo-Json -Depth 4
    Invoke-KubectlApply -Manifest $secretManifest
}
finally {
    $rdsPassword = $null
    $jwtKey = $null
    $avisoSigningKey = $null
    $mongoConnectionString = $null
    $mongoConnectionStringBase64 = $null
    $connectionString = $null
    $firebaseBase64 = $null
    $smtpExisting = $null
    $optionalSecretData = $null
}

$migrationPath = Join-Path $terraformDirectory "k8s\base\migration.yaml"
$migrationManifest = (Get-Content -Raw $migrationPath).Replace(
    "tallernet/backend:bootstrap",
    "$($repositories.backend):$ImageTag"
)

& kubectl -n $Namespace delete job database-migration --ignore-not-found
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Invoke-KubectlApply -Manifest $migrationManifest

& kubectl -n $Namespace wait --for=condition=complete job/database-migration --timeout=5m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$basePath = Join-Path $terraformDirectory "k8s\base"
$applicationManifest = ((& kubectl kustomize $basePath) -join [Environment]::NewLine).
    Replace("tallernet/backend:bootstrap", "$($repositories.backend):$ImageTag").
    Replace("tallernet/frontend:bootstrap", "$($repositories.frontend):$ImageTag")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Invoke-KubectlApply -Manifest $applicationManifest

# Métricas de CPU que necesita el HPA del Backoffice.
& kubectl apply -f (Join-Path $terraformDirectory "k8s\metrics-server\components.yaml")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& kubectl -n $Namespace rollout status deployment/backoffice --timeout=5m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& kubectl -n $Namespace rollout status deployment/portal --timeout=5m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($EnableWorker) {
    & kubectl -n $Namespace set image deployment/queue-worker "queue-worker=$($repositories.worker):$ImageTag"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & kubectl -n $Namespace scale deployment/queue-worker --replicas=1
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & kubectl -n $Namespace rollout status deployment/queue-worker --timeout=5m
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& kubectl -n $Namespace rollout status deployment/tallernet-otel-lgtm --timeout=5m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

foreach ($serviceName in @("backoffice", "portal", "tallernet-rabbitmq", "tallernet-redis", "tallernet-otel-lgtm")) {
    & kubectl -n $Namespace get service $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& kubectl -n $Namespace get ingress tallernet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }