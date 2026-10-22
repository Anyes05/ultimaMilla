param(
    [string]$ClusterName = "tallernet-cluster",
    [string]$NodeGroupName = "tallernet-nodes",
    [string]$TargetGroupName = "tallernet-lab-nginx",
    [int]$MinimumSize = 2,
    [int]$DesiredSize = 2,
    [int]$MaximumSize = 4,
    [string]$Region,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name es requerido para reanudar el laboratorio."
    }
}

function Get-NodeGroup {
    return (& aws eks describe-nodegroup --cluster-name $ClusterName --nodegroup-name $NodeGroupName --output json | ConvertFrom-Json).nodegroup
}

function Wait-NodeGroupActive {
    & aws eks wait nodegroup-active --cluster-name $ClusterName --nodegroup-name $NodeGroupName
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
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
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$targetHealth = @(& aws elbv2 describe-target-health --region $Region --target-group-arn $targetGroupArn --output json | ConvertFrom-Json).TargetHealthDescriptions
$healthyTargetCount = @($targetHealth | Where-Object { $_.TargetHealth.State -eq "healthy" }).Count

if (-not $Force -and $inServiceCount -ge $DesiredSize -and $healthyTargetCount -ge $DesiredSize) {
    Write-Host "El laboratorio ya esta listo: $inServiceCount nodos InService y $healthyTargetCount targets ALB saludables."
    exit 0
}

Write-Host "Recuperando node group $NodeGroupName. Estado actual: $($nodeGroup.status), nodos InService: $inServiceCount, targets saludables: $healthyTargetCount."

& aws eks update-nodegroup-config --region $Region `
    --cluster-name $ClusterName `
    --nodegroup-name $NodeGroupName `
    --scaling-config "minSize=0,maxSize=$MaximumSize,desiredSize=0" | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Wait-NodeGroupActive

& aws eks update-nodegroup-config --region $Region `
    --cluster-name $ClusterName `
    --nodegroup-name $NodeGroupName `
    --scaling-config "minSize=$MinimumSize,maxSize=$MaximumSize,desiredSize=$DesiredSize" | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Wait-NodeGroupActive

& aws eks update-kubeconfig --region $Region --name $ClusterName
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& kubectl wait --for=condition=Ready node --all --timeout=10m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$ingressController = & kubectl --namespace ingress-nginx get deployment ingress-nginx-controller --ignore-not-found -o name
if ([string]::IsNullOrWhiteSpace(($ingressController | Out-String))) {
    throw "NGINX Ingress no esta instalado. Ejecute Deploy-Lab.ps1 para completar la fase de plataforma antes de reanudar el laboratorio."
}

& kubectl --namespace ingress-nginx rollout status deployment/ingress-nginx-controller --timeout=10m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& aws elbv2 wait target-in-service --region $Region --target-group-arn $targetGroupArn
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Laboratorio reanudado. EKS y el ALB recuperaron los nodos automaticamente."