param([Parameter(Mandatory)] [string] $Exe, [string] $Folder = $env:TEMP)

$ErrorActionPreference = 'Stop'
if (Get-Process -Name QuadPane -ErrorAction SilentlyContinue) {
    throw 'Hãy thoát QuadPane trước khi chạy kiểm tra.'
}

$first = Start-Process -FilePath $Exe -PassThru
try {
    Start-Sleep -Seconds 3
    if ($first.HasExited) { throw 'Phiên bản đầu đã thoát.' }
    $second = Start-Process -FilePath $Exe -ArgumentList ('"' + $Folder + '"') -PassThru
    if (-not $second.WaitForExit(5000)) {
        $second.Kill()
        throw 'Phiên bản thứ hai không thoát.'
    }
    if ($first.HasExited -or @(Get-Process -Name QuadPane -ErrorAction SilentlyContinue).Count -ne 1) {
        throw 'Đã mở nhiều hơn một phiên bản.'
    }
    Write-Host 'Single-instance folder command OK'
} finally {
    if (-not $first.HasExited) { $first.Kill() }
}
