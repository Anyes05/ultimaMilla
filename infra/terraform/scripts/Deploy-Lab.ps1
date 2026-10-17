param(
    [ValidateSet("Plan", "Apply")]
    [string]$Action = "Apply",
    [switch]$ValidateManualDns,
    [ValidatePattern("^[0-9a-f]{40}$")]
    [string]$ApplicationImageTag
)

$ErrorActionPreference = "Stop"

Set-Location (Split-Path -Parent $PSScriptRoot)

. (Join-Path $PSScriptRoot "Import-Environment.ps1")

function Set-SensitiveEnvironmentVariable {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [string]$Prompt
    )

    if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($Name, "Process"))) {
        return
    }

    $secureValue = Read-Host -Prompt $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)

    try {
        [Environment]::SetEnvironmentVariable(
            $Name,
            [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer),
            "Process"
        )
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Show-ManualDnsRecords {
    param(
        [Parameter(Mandatory)]
        [string]$OutputName,
        [Parameter(Mandatory)]
        [string]$Heading
    )

    Write-Host ""
    Write-Host $Heading
    $records = @(& terraform output -json $OutputName | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    foreach ($record in $records) {
        Write-Host ""
        Write-Host "Zona:  $($record.zone)"
        Write-Host "Host:  $($record.name)"
        Write-Host "Tipo:  $($record.type)"
        Write-Host "Valor: $($record.value)"
        Write-Host "TTL:   $($record.ttl)"
    }
}

function Invoke-TerraformApplyPhase {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Write-Host ""
    Write-Host "=== $Name ==="
    & terraform apply -auto-approve @Arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

function Confirm-ManualCertificateRecords {
    param(
        [Parameter(Mandatory)]
        [string]$Prompt
    )

    while ($true) {
        $answer = Read-Host $Prompt
        if ($answer -ceq "CONTINUAR") {
            return
        }

        Write-Host "La validacion sigue detenida. Escriba CONTINUAR cuando los registros esten listos."
    }
}

function Initialize-NginxHelmRepository {
    if ($null -eq (Get-Command helm -ErrorAction SilentlyContinue)) {
        throw "Helm CLI es requerido para instalar ingress-nginx. Instale Helm y ejecute el script nuevamente."
    }

    & helm repo add ingress-nginx "https://kubernetes.github.io/ingress-nginx" --force-update
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & helm repo update ingress-nginx
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

function Get-AcmCertificateStatus {
    $certificateState = & terraform state show -no-color "aws_acm_certificate.public"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $arnMatch = $certificateState | Select-String '^\s*arn\s*=\s*"([^"]+)"$'
    if (-not $arnMatch) {
        throw "No se pudo obtener el ARN del certificado ACM desde el state de Terraform."
    }

    $certificateArn = $arnMatch.Matches[0].Groups[1].Value
    $awsRegion = $env:TF_VAR_aws_region
    if ([string]::IsNullOrWhiteSpace($awsRegion)) { $awsRegion = $env:AWS_REGION }
    if ([string]::IsNullOrWhiteSpace($awsRegion)) { $awsRegion = $env:AWS_DEFAULT_REGION }
    if ([string]::IsNullOrWhiteSpace($awsRegion)) { $awsRegion = "us-east-1" }

    $status = & aws acm describe-certificate `
        --certificate-arn $certificateArn `
        --region $awsRegion `
        --query "Certificate.Status" `
        --output text
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    return $status.Trim()
}

function Move-HttpListenerState {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$StateResources
    )

    $legacyAddresses = @(
        "aws_lb_listener.http_forward[0]",
        "aws_lb_listener.http"
    )
    $legacyAddress = $legacyAddresses | Where-Object { $_ -in $StateResources } | Select-Object -First 1

    if ($null -eq $legacyAddress) {
        return
    }

    Write-Host "Migrando el listener HTTP existente al recurso de redireccion HTTPS en el state."
    & terraform state mv $legacyAddress "aws_lb_listener.http_redirect"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

function Move-LegacyCertificateState {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$StateResources
    )

    if (
        "aws_acm_certificate.public" -notin $StateResources -or
        "aws_acm_certificate.legacy[0]" -in $StateResources
    ) {
        return
    }

    Write-Host "Conservando temporalmente el certificado anterior mientras se valida el wildcard."
    & terraform state mv "aws_acm_certificate.public" "aws_acm_certificate.legacy[0]"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& terraform init -upgrade
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Initialize-NginxHelmRepository

& terraform fmt -check -recursive
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& terraform validate
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Terraform solicita la contrasena de RDS antes de evaluar la configuracion; RDS se crea recien en su fase dedicada."
Set-SensitiveEnvironmentVariable -Name "TF_VAR_database_master_password" -Prompt "Contrasena maestra de PostgreSQL"

$fullTerraformArguments = @(
    "-input=false",
    "-var=install_platform=true"
)
$stateResources = @(& terraform state list)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Action -eq "Plan") {
    & terraform plan @fullTerraformArguments

    exit $LASTEXITCODE
}

Move-HttpListenerState -StateResources $stateResources

$stateResources = @(& terraform state list)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$phaseTerraformArguments = @($fullTerraformArguments)
if ("aws_acm_certificate.legacy[0]" -in $stateResources) {
    $phaseTerraformArguments += "-var=retain_legacy_certificate=true"
}

$certificateArguments = $phaseTerraformArguments + @("-target=aws_acm_certificate.public")
$certificateValidationArguments = $phaseTerraformArguments + @("-target=aws_acm_certificate_validation.public")
$securityGroupArguments = $phaseTerraformArguments + @(
    "-target=aws_security_group.eks_cluster",
    "-target=aws_security_group.eks_nodes",
    "-target=aws_security_group.alb_public",
    "-target=aws_security_group.rds_postgres"
)
$databaseAndRegistryArguments = $phaseTerraformArguments + @(
    "-target=aws_db_instance.postgres",
    "-target=aws_ecr_repository.frontend",
    "-target=aws_ecr_repository.backend",
    "-target=aws_ecr_repository.worker"
)
$clusterArguments = $phaseTerraformArguments + @("-target=aws_eks_cluster.main")
$nodeGroupArguments = $phaseTerraformArguments + @(
    "-target=aws_vpc_security_group_ingress_rule.cluster_api_from_nodes",
    "-target=aws_eks_node_group.main"
)
$loadBalancerArguments = $phaseTerraformArguments + @(
    "-target=aws_lb.public",
    "-target=aws_lb_target_group.nginx_ingress",
    "-target=aws_autoscaling_attachment.nginx_ingress",
    "-target=aws_lb_listener.http_redirect",
    "-target=aws_lb_listener.https"
)

Invoke-TerraformApplyPhase -Name "Fase 1 de 8: solicitar o actualizar certificado ACM" -Arguments $certificateArguments

 $certificateStatus = Get-AcmCertificateStatus

if ($certificateStatus -ne "ISSUED") {
    Show-ManualDnsRecords -OutputName "manual_acm_validation_records" -Heading "CNAME de validacion ACM wildcard"

    if ($ValidateManualDns) {
        throw "El certificado ACM wildcard aun no esta emitido. Cree el CNAME mostrado, espere su propagacion y ejecute el script nuevamente con -ValidateManualDns."
    }

    Confirm-ManualCertificateRecords -Prompt "Cree el CNAME de validacion anterior en Cloudns y escriba CONTINUAR cuando haya propagado"
}

Invoke-TerraformApplyPhase -Name "Fase 2 de 8: esperar certificado ACM emitido" -Arguments $certificateValidationArguments

Invoke-TerraformApplyPhase -Name "Fase 3 de 8: crear grupos de seguridad" -Arguments $securityGroupArguments
Invoke-TerraformApplyPhase -Name "Fase 4 de 8: crear RDS y repositorios ECR" -Arguments $databaseAndRegistryArguments
Invoke-TerraformApplyPhase -Name "Fase 5 de 8: crear control plane EKS" -Arguments $clusterArguments
Invoke-TerraformApplyPhase -Name "Fase 6 de 8: crear node group EKS" -Arguments $nodeGroupArguments
Invoke-TerraformApplyPhase -Name "Fase 7 de 8: crear ALB con HTTPS" -Arguments $loadBalancerArguments

Show-ManualDnsRecords -OutputName "manual_application_dns_records" -Heading "CNAMEs publicos hacia el ALB"
Write-Host "En Cloudns cree todos los CNAME mostrados, incluidos *, app, api y tracking. Copie Host y Valor exactamente como se muestran y use TTL 3600 (1 Hour)."
Confirm-ManualCertificateRecords -Prompt "Cree todos los CNAME anteriores en Cloudns y escriba CONTINUAR cuando haya guardado los registros"

Invoke-TerraformApplyPhase -Name "Fase 8 de 8: instalar NGINX, RabbitMQ, Redis y worker" -Arguments $fullTerraformArguments

if ([string]::IsNullOrWhiteSpace($ApplicationImageTag)) {
    Write-Host "La plataforma esta lista. Despues de publicar imagenes en ECR, ejecute .\scripts\Deploy-Applications.ps1 -ImageTag <SHA-de-40-caracteres>."
}
else {
    & (Join-Path $PSScriptRoot "Deploy-Applications.ps1") -ImageTag $ApplicationImageTag
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

exit $LASTEXITCODE