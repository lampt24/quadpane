# Run with Windows PowerShell 5.1 or pwsh; all operations use disposable fixtures.
$ErrorActionPreference = 'Stop'
$updater = Join-Path $PSScriptRoot '../Updater.ps1'
$tokens = $null
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile($updater, [ref]$tokens, [ref]$parseErrors) | Out-Null
if ($parseErrors.Count) { throw ($parseErrors.Message -join "`n") }

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('QuadPane-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    foreach ($mode in @('rename', 'in-place', 'copy-failure', 'stage-failure')) {
        $case = Join-Path $fixture $mode
        $install = Join-Path $case 'install [test]'
        $stage = Join-Path $case 'stage [test]'
        $backup = Join-Path $case 'backup'
        $script = Join-Path $case 'updater.ps1'
        New-Item -ItemType Directory -Path $install, $stage -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $install 'QuadPane.exe'), 'old')
        [IO.File]::WriteAllText((Join-Path $stage 'QuadPane.exe'), 'new')
        [IO.File]::WriteAllText((Join-Path $install 'library.dll'), 'old-library')
        [IO.File]::WriteAllText((Join-Path $stage 'library.dll'), 'new-library')
        Copy-Item -LiteralPath $updater -Destination $script
        $result = @{ Restarted = $false }
        function Get-Process {
            param($Id, $Name, $ErrorAction)
            if (-not (Test-Path -LiteralPath "$script.ready")) { throw 'Updater did not acknowledge readiness before waiting for app exit.' }
        }
        function Start-Sleep { param($Milliseconds) }
        function Add-Type { throw 'Test suppresses error dialogs.' }
        function Start-Process {
            param($FilePath, $WorkingDirectory)
            if ([IO.File]::ReadAllText($FilePath) -ne 'new') { throw 'Restarted old executable.' }
            $result.Restarted = $true
        }
        function Move-Item {
            param($LiteralPath, $Destination, [switch]$Force, $ErrorAction)
            if ($mode -in @('in-place', 'copy-failure') -and $LiteralPath -eq $install) { throw 'Simulated directory lock.' }
            if ($mode -eq 'stage-failure' -and $LiteralPath -eq $stage) { throw 'Simulated stage move failure.' }
            Microsoft.PowerShell.Management\Move-Item @PSBoundParameters
        }
        function Copy-Item {
            [CmdletBinding()]
            param([Parameter(ValueFromPipeline)]$InputObject, $LiteralPath, $Path, $Destination, [switch]$Recurse, [switch]$Force)
            process {
                if ($mode -eq 'copy-failure' -and $Destination -eq $install -and
                    (($InputObject -and $InputObject.FullName.StartsWith($stage)) -or $LiteralPath -eq $stage -or "$Path".StartsWith($stage))) {
                    [IO.File]::WriteAllText((Join-Path $install 'QuadPane.exe'), 'partial-copy')
                    throw 'Simulated copy failure after modifying executable.'
                }
                if ($InputObject) { $InputObject | Microsoft.PowerShell.Management\Copy-Item -Destination $Destination -Recurse:$Recurse -Force:$Force -ErrorAction Stop }
                else { Microsoft.PowerShell.Management\Copy-Item @PSBoundParameters }
            }
        }
        $failed = $false
        try { & $script -AppProcessId 2147483647 -InstallPath $install -StagePath $stage -BackupPath $backup -ScriptPath $script }
        catch { $failed = $true; if ($mode -notin @('copy-failure', 'stage-failure')) { throw } }
        if ($mode -in @('copy-failure', 'stage-failure')) {
            if (-not $failed -or $result.Restarted -or
                [IO.File]::ReadAllText((Join-Path $install 'QuadPane.exe')) -ne 'old' -or
                [IO.File]::ReadAllText((Join-Path $install 'library.dll')) -ne 'old-library') {
                throw 'Failed update did not preserve the previous installation.'
            }
        } elseif ($failed -or -not $result.Restarted -or
            [IO.File]::ReadAllText((Join-Path $backup 'QuadPane.exe')) -ne 'old') {
            throw "Update scenario failed: $mode"
        }
        if (Test-Path -LiteralPath "$script.ready") { throw 'Readiness file was not cleaned up.' }
        Write-Host "Updater OK: $mode"
        Remove-Item Function:Copy-Item, Function:Move-Item, Function:Get-Process, Function:Start-Sleep, Function:Start-Process, Function:Add-Type
    }
} finally {
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $fixture -Recurse -Force
}
