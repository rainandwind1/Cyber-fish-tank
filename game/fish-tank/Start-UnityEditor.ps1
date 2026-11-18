param(
    [string]$UnityExe = 'E:\unityhub\editor\6000.3.28f1\Editor\Unity.exe'
)

$projectPath = $PSScriptRoot
$logDirectory = Join-Path $projectPath 'Logs'
$logPath = Join-Path $logDirectory 'Editor.log'
$maxLogBytes = 512MB
$maxTotalLogBytes = 1GB
$maxLogAge = [TimeSpan]::FromDays(14)

function Clear-UnityEditorLogs {
    $cutoff = (Get-Date) - $maxLogAge
    $logs = @(Get-ChildItem -LiteralPath $logDirectory -Filter 'Editor*.log' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending)

    foreach ($log in $logs) {
        if ($log.LastWriteTime -lt $cutoff -or $log.Length -gt $maxLogBytes) {
            Remove-Item -LiteralPath $log.FullName -Force
        }
    }

    $logs = @(Get-ChildItem -LiteralPath $logDirectory -Filter 'Editor*.log' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending)
    $totalBytes = ($logs | Measure-Object -Property Length -Sum).Sum
    foreach ($log in ($logs | Sort-Object LastWriteTime)) {
        if ($totalBytes -le $maxTotalLogBytes) { break }
        Remove-Item -LiteralPath $log.FullName -Force
        $totalBytes -= $log.Length
    }
}

if (-not (Test-Path -LiteralPath $UnityExe)) {
    throw "Unity Editor not found: $UnityExe"
}

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
Clear-UnityEditorLogs
$editor = Start-Process -FilePath $UnityExe -PassThru -ArgumentList @(
    '-projectPath', "`"$projectPath`"",
    '-logFile', "`"$logPath`""
)
$editor.WaitForExit()
Clear-UnityEditorLogs
