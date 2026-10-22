[CmdletBinding()]
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

$resumeLabScript = Join-Path $PSScriptRoot "..\infra\terraform\scripts\Resume-Lab.ps1"
& $resumeLabScript @PSBoundParameters
exit $LASTEXITCODE