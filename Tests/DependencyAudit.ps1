param([string]$Dotnet = 'dotnet', [string]$Project = 'MediaStudio.csproj')
$ErrorActionPreference = 'Stop'
$lines = & $Dotnet list $Project package --vulnerable --include-transitive --format json 2>&1
if ($LASTEXITCODE -ne 0) { throw "Dependency audit did not complete: $($lines -join [Environment]::NewLine)" }
try { $report = ($lines -join [Environment]::NewLine) | ConvertFrom-Json } catch { throw 'Dependency audit returned an invalid report' }
if ($report.problems.Count -gt 0) { throw 'Dependency audit reported errors or incomplete vulnerability data' }
if ($report.version -ne 1 -or !$report.projects -or !$report.sources) { throw 'Dependency audit report is incomplete' }
$vulnerable = @($report.projects | ForEach-Object { $_.frameworks } | ForEach-Object { @($_.topLevelPackages) + @($_.transitivePackages) } | Where-Object { $_ -and $_.vulnerabilities.Count -gt 0 })
if ($vulnerable.Count -gt 0) { throw ('Known vulnerable packages: ' + (($vulnerable | ForEach-Object { $_.id + ' ' + $_.resolvedVersion }) -join ', ')) }
Write-Output 'PASS: direct and transitive package audit completed; no known vulnerable packages reported.'
