param(
    [string]$ClusterName = "tallernet-cluster",
    [string]$NodeGroupName = "tallernet-nodes",
    [string]$TargetGroupName = "tallernet-lab-nginx",
    [int]$MinimumSize = 2,
    [int]$DesiredSize = 2,
    [int]$MaximumSize = 4,
    [string]$Region,
    [switch]$Force,
    [int]$TargetWaitTimeoutSeconds = 600,
    [int]$TargetPollIntervalSeconds = 15
)

$ErrorActionPreference = "Stop"

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name es requerido para reanudar el laboratorio."
    }
}

function Get-NodeGroup {
    return (& aws eks describe-nodegroup --region $Region --cluster-name $ClusterName --nodegroup-name $NodeGroupName --output json | ConvertFrom-Json).nodegroup
}

function Wait-NodeGroupActive {
    & aws eks wait nodegroup-active --region $Region --cluster-name $ClusterName --nodegroup-name $NodeGroupName
    if ($LASTEXITCODE -ne 0) { throw "El node group $NodeGroupName no alcanzo el estado ACTIVE." }
}

function Get-TargetHealth {
    return @((& aws elbv2 describe-target-health --region $Region --target-group-arn $targetGroupArn --output json | ConvertFrom-Json).TargetHealthDescriptions)
}

function Get-HealthyTargetCount {
    return @(Get-TargetHealth | Where-Object { $_.TargetHealth.State -eq "healthy" }).Count
}

function Wait-HealthyTargets {
    $deadline = (Get-Date).AddSeconds($TargetWaitTimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        $targetHealth = Get-TargetHealth
        $healthyTargetCount = @($targetHealth | Where-Object { $_.TargetHealth.State -eq "healthy" }).Count

        if ($healthyTargetCount -ge $DesiredSize) {
            Write-Host "El ALB tiene $healthyTargetCount targets saludables."
            return
        }

        $targetStates = if ($targetHealth.Count -eq 0) {
            "sin targets registrados"
        }
        else {
            ($targetHealth | ForEach-Object {
                $reason = $_.TargetHealth.Reason
                if ([string]::IsNullOrWhiteSpace($reason)) { $reason = "sin detalle" }
                "$($_.Target.Id): $($_.TargetHealth.State) ($reason)"
            }) -join "; "
        }

        Write-Host "Esperando targets del ALB: $healthyTargetCount/$DesiredSize saludables; $targetStates"
        Start-Sleep -Seconds $TargetPollIntervalSeconds
    }

    throw "El ALB no alcanzo $DesiredSize targets saludables en $TargetWaitTimeoutSeconds segundos. Revise el estado informado arriba y ejecute de nuevo este script cuando se haya corregido la causa."
}

Assert-Command -Name "aws"
Assert-Command -Name "kubectl"

if ([string]::IsNullOrWhiteSpace($Region)) {
    $Region = (& aws configure get region).Trim()
}
if ([string]::IsNullOrWhiteSpace($Region)) { $Region = "us-east-1" }

$nodeGroup = Get-NodeGroup
$autoScalingGroupName = $nodeGroup.resources.autoScalingGroups[0].name
$autoScalingGroup = (& aws autoscaling describe-auto-scaling-groups --auto-scaling-group-names $autoScalingGroupName --output json | ConvertFrom-Json).AutoScalingGroups[0]
$inServiceCount = @($autoScalingGroup.Instances | Where-Object { $_.LifecycleState -eq "InService" }).Count

$targetGroupArn = & aws elbv2 describe-target-groups --region $Region --names $TargetGroupName --query "TargetGroups[0].TargetGroupArn" --output text
if ($LASTEXITCODE -ne 0) { throw "No se encontro el target group $TargetGroupName." }
$healthyTargetCount = Get-HealthyTargetCount

if (-not $Force -and $inServiceCount -ge $DesiredSize -and $healthyTargetCount -ge $DesiredSize) {
    Write-Host "El laboratorio ya esta listo: $inServiceCount nodos InService y $healthyTargetCount targets ALB saludables."
    exit 0
}

if ($Force -or $inServiceCount -lt $DesiredSize) {
    Write-Host "Recuperando node group $NodeGroupName. Estado actual: $($nodeGroup.status), nodos InService: $inServiceCount."

    & aws eks update-nodegroup-config --region $Region `
        --cluster-name $ClusterName `
        --nodegroup-name $NodeGroupName `
        --scaling-config "minSize=0,maxSize=$MaximumSize,desiredSize=0" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No se pudo reducir temporalmente el node group $NodeGroupName." }
    Wait-NodeGroupActive

    & aws eks update-nodegroup-config --region $Region `
        --cluster-name $ClusterName `
        --nodegroup-name $NodeGroupName `
        --scaling-config "minSize=$MinimumSize,maxSize=$MaximumSize,desiredSize=$DesiredSize" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No se pudo escalar el node group $NodeGroupName." }
    Wait-NodeGroupActive
}
else {
    Write-Host "Los $inServiceCount nodos ya estan disponibles; se conserva el node group y solo se recupera Kubernetes/ALB."
}

& aws eks update-kubeconfig --region $Region --name $ClusterName
if ($LASTEXITCODE -ne 0) { throw "No se pudo actualizar kubeconfig para $ClusterName." }
& kubectl wait --for=condition=Ready node --all --timeout=10m
if ($LASTEXITCODE -ne 0) { throw "Los nodos no alcanzaron el estado Ready." }

$ingressController = & kubectl --namespace ingress-nginx get deployment ingress-nginx-controller --ignore-not-found -o name
if ([string]::IsNullOrWhiteSpace(($ingressController | Out-String))) {
    throw "NGINX Ingress no esta instalado. Ejecute Deploy-Lab.ps1 para completar la fase de plataforma antes de reanudar el laboratorio."
}

& kubectl --namespace ingress-nginx rollout status deployment/ingress-nginx-controller --timeout=10m
if ($LASTEXITCODE -ne 0) { throw "NGINX Ingress no termino su rollout." }

Wait-HealthyTargets

Write-Host "Laboratorio reanudado. EKS y el ALB recuperaron los nodos automaticamente."