[CmdletBinding()]
param([string]$BaseUrl = 'http://127.0.0.1:5253')
$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')
$health = Invoke-RestMethod -Uri "$BaseUrl/health"
if (!$health.demo) { throw 'Start scripts/Start-Demo.ps1 first. This demonstration writes only to a demo journal.' }
$workoutBody = @{ name = 'Plate builder API demo'; date = (Get-Date -Format 'yyyy-MM-dd') } | ConvertTo-Json
$workout = Invoke-RestMethod -Uri "$BaseUrl/api/workouts" -Method Post -ContentType 'application/json' -Body $workoutBody
$examples = @(
    @{ clientId = [guid]::NewGuid().ToString(); exerciseId = 'bench-press'; unit = 'Pounds'; barWeight = 45; leftPlates = @(45,25,10); rightPlates = @(45,25,10); reps = 5; rpe = 8; kind = 'Working'; notes = 'Signature per-side plate example' },
    @{ clientId = [guid]::NewGuid().ToString(); exerciseId = 'back-squat'; unit = 'Kilograms'; barWeight = 20; leftPlates = @(20,5,1.25); rightPlates = @(20,5,1.25); reps = 5; rpe = 7; kind = 'Working'; notes = 'Fractional metric plate example' }
)
foreach ($example in $examples) {
    $saved = Invoke-RestMethod -Uri "$BaseUrl/api/workouts/$($workout.id)/sets" -Method Post -ContentType 'application/json' -Body ($example | ConvertTo-Json)
}
if ($saved.sets[0].totalWeight -ne 205 -or $saved.sets[1].totalWeight -ne 72.5) { throw 'The plate calculation did not match its expected result.' }
$completed = Invoke-RestMethod -Uri "$BaseUrl/api/workouts/$($workout.id)/complete" -Method Post
$outputDirectory = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path '.local'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$output = Join-Path $outputDirectory 'plate-demo.csv'
Invoke-WebRequest -Uri "$BaseUrl/api/workouts/export.csv" -OutFile $output | Out-Null
$completed.sets | Select-Object exerciseName,totalWeight,unit,reps,plateLabel | Format-Table -AutoSize
Write-Host "Completed demo workout $($completed.id). CSV written to $output"
