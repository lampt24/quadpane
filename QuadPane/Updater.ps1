param(
    [Parameter(Mandatory)] [int] $AppProcessId,
    [Parameter(Mandatory)] [string] $InstallPath,
    [Parameter(Mandatory)] [string] $StagePath,
    [Parameter(Mandatory)] [string] $BackupPath,
    [Parameter(Mandatory)] [string] $ScriptPath
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Write-Log([string]$msg) {
    try {
        Add-Content -Path (Join-Path $env:TEMP 'QuadPane-update.log') -Value "$(Get-Date -Format o) $msg" -Encoding UTF8
    } catch { }
}

Write-Log "Bắt đầu cập nhật QuadPane. ProcessId: $AppProcessId, Install: $InstallPath, Stage: $StagePath"

try {
    # 1. Chờ tiến trình chính của QuadPane thoát (tối đa 10 giây)
    $app = Get-Process -Id $AppProcessId -ErrorAction SilentlyContinue
    if ($app) {
        $exited = $app.WaitForExit(10000)
        if (-not $exited) {
            Write-Log "Tiến trình $AppProcessId không thoát sau 10s, đang buộc dừng..."
            Stop-Process -Id $AppProcessId -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
        }
    }

    # 2. Dừng bất kỳ tiến trình QuadPane nào khác đang chạy từ thư mục cài đặt
    Get-Process -Name 'QuadPane' -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ($_.Path -and ($_.Path -like "$InstallPath*")) {
                Write-Log "Dừng tiến trình QuadPane (PID: $($_.Id)) chạy từ $InstallPath"
                Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            }
        } catch { }
    }

    # Chờ để hệ điều hành và Windows Defender giải phóng hoàn toàn các file handle
    Start-Sleep -Milliseconds 800

    # 3. Thử đổi tên thư mục cài đặt thành BackupPath (Atomic directory rename)
    # Thử lặp lại nhiều lần nếu file đang bận nhả handle
    $dirMoved = $false
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            Move-Item -LiteralPath $InstallPath -Destination $BackupPath -Force -ErrorAction Stop
            $dirMoved = $true
            Write-Log "Đã di chuyển thư mục InstallPath -> BackupPath ở lần thử $attempt"
            break
        } catch {
            Write-Log "Lần thử $attempt: Move-Item InstallPath thất bại: $_"
            Start-Sleep -Milliseconds 500
        }
    }

    if ($dirMoved) {
        # Đổi tên thành công: Di chuyển StagePath thành InstallPath
        $stageMoved = $false
        for ($attempt = 1; $attempt -le 20; $attempt++) {
            try {
                Move-Item -LiteralPath $StagePath -Destination $InstallPath -Force -ErrorAction Stop
                $stageMoved = $true
                Write-Log "Đã di chuyển StagePath -> InstallPath ở lần thử $attempt"
                break
            } catch {
                Write-Log "Lần thử $attempt: Move-Item StagePath thất bại: $_"
                Start-Sleep -Milliseconds 500
            }
        }

        if (-not $stageMoved) {
            # Rollback
            Write-Log "Di chuyển StagePath thất bại, đang rollback từ BackupPath..."
            if (Test-Path -LiteralPath $InstallPath) {
                Remove-Item -LiteralPath $InstallPath -Recurse -Force -ErrorAction SilentlyContinue
            }
            Move-Item -LiteralPath $BackupPath -Destination $InstallPath -Force -ErrorAction SilentlyContinue
            throw "Không thể chuyển tệp mới vào $InstallPath."
        }
    } else {
        # Nếu không thể đổi tên cả thư mục (do có Explorer hoặc cửa sổ terminal đang mở trong InstallPath),
        # ta áp dụng cơ chế sao chép đè từng tệp (in-place file replacement)
        Write-Log "Không thể di chuyển cả thư mục, chuyển sang chế độ cập nhật từng tệp in-place..."

        if (-not (Test-Path -LiteralPath $BackupPath)) {
            New-Item -ItemType Directory -LiteralPath $BackupPath -Force | Out-Null
        }

        # Backup các tệp hiện tại
        try {
            Copy-Item -Path "$InstallPath\*" -Destination $BackupPath -Recurse -Force -ErrorAction SilentlyContinue
        } catch { }

        # Copy đè các tệp mới từ StagePath vào InstallPath với retry loop
        $filesCopied = $false
        for ($attempt = 1; $attempt -le 20; $attempt++) {
            try {
                Copy-Item -Path "$StagePath\*" -Destination $InstallPath -Recurse -Force -ErrorAction Stop
                $filesCopied = $true
                Write-Log "Đã copy đè tệp mới từ StagePath -> InstallPath ở lần thử $attempt"
                break
            } catch {
                Write-Log "Lần thử $attempt: Copy-Item tệp mới thất bại: $_"
                Start-Sleep -Milliseconds 600
            }
        }

        if (-not $filesCopied) {
            throw "Không thể ghi đè các tệp trong thư mục $InstallPath. Vui lòng đóng tất cả cửa sổ Explorer hoặc Terminal đang mở tại thư mục này rồi thử lại."
        }
    }

    # Dọn dẹp thư mục tạm StagePath
    if (Test-Path -LiteralPath $StagePath) {
        Remove-Item -LiteralPath $StagePath -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Khởi động lại QuadPane mới
    $exePath = Join-Path $InstallPath 'QuadPane.exe'
    Write-Log "Khởi động lại QuadPane: $exePath"
    Start-Process -FilePath $exePath -WorkingDirectory $InstallPath

} catch {
    $err = "$_"
    Write-Log "Lỗi cập nhật: $err"
    try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show("Không thể cài bản cập nhật:`n$err", 'QuadPane', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    } catch { }
} finally {
    Remove-Item -LiteralPath $ScriptPath -Force -ErrorAction SilentlyContinue
}
