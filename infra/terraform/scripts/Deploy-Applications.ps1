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

try {
    $connectionString = "Host=$rdsEndpoint;Port=5432;Database=tallernet;Username=tallernet_admin;Password=$rdsPassword"
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
        }
    } | ConvertTo-Json -Depth 4
    Invoke-KubectlApply -Manifest $secretManifest
}
finally {
    $rdsPassword = $null
    $jwtKey = $null
    $avisoSigningKey = $null
    $connectionString = $null
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

foreach ($serviceName in @("backoffice", "portal", "tallernet-rabbitmq", "tallernet-redis")) {
    & kubectl -n $Namespace get service $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& kubectl -n $Namespace get ingress tallernet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }