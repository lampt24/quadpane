param(
    [Parameter(Mandatory)] [int] $AppProcessId,
    [Parameter(Mandatory)] [string] $InstallPath,
    [Parameter(Mandatory)] [string] $StagePath,
    [Parameter(Mandatory)] [string] $BackupPath,
    [Parameter(Mandatory)] [string] $ScriptPath
)

$ErrorActionPreference = 'Stop'
try {
    $app = Get-Process -Id $AppProcessId -ErrorAction SilentlyContinue
    if ($app) { $app.WaitForExit() }
    Move-Item -LiteralPath $InstallPath -Destination $BackupPath
    try {
        Move-Item -LiteralPath $StagePath -Destination $InstallPath
        Start-Process -FilePath (Join-Path $InstallPath 'QuadPane.exe') -WorkingDirectory $InstallPath
    } catch {
        if (Test-Path -LiteralPath $InstallPath) { Remove-Item -LiteralPath $InstallPath -Recurse -Force }
        Move-Item -LiteralPath $BackupPath -Destination $InstallPath
        throw
    }
    # ponytail: Giữ bản cũ để tránh mất file người dùng; chỉ dọn sau khi có quy trình xác nhận bản mới chạy ổn.
} catch {
    Add-Content -Path (Join-Path $env:TEMP 'QuadPane-update.log') -Value "$(Get-Date -Format o) $_"
    try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show("Không thể cài bản cập nhật: $_", 'QuadPane') | Out-Null
    } catch { }
} finally {
    Remove-Item -LiteralPath $ScriptPath -Force -ErrorAction SilentlyContinue
}
