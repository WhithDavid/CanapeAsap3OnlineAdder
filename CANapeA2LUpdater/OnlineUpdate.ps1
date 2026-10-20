param(
    [Parameter(Mandatory = $true)]
    [string]$Project,

    [Parameter(Mandatory = $true)]
    [string]$Elf,

    [string]$Measurements,
    [string]$Calibrations,
    [string]$Module = 'XCPsim'
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (Test-Path (Join-Path $scriptDir 'Asap3ReadOnlyProbe.exe')) {
    $binDir = $scriptDir
} else {
    $binDir = Join-Path $scriptDir 'bin'
}
$probeExe = Join-Path $binDir 'Asap3ReadOnlyProbe.exe'
$coreExe = Join-Path $binDir 'CanapeA2LUpdater.exe'
$applyExe = Join-Path $binDir 'CanapeOnlineApply.exe'
$probeScript = Join-Path $binDir 'probe_script_channel.cns'

foreach ($required in @($Project, $Elf, $probeExe, $coreExe, $applyExe, $probeScript)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file does not exist: $required"
    }
}
if ([string]::IsNullOrWhiteSpace($Measurements) -and [string]::IsNullOrWhiteSpace($Calibrations)) {
    throw 'At least one measurement or calibration list is required.'
}
if (-not [string]::IsNullOrWhiteSpace($Measurements) -and -not (Test-Path -LiteralPath $Measurements -PathType Leaf)) {
    throw "Measurement list does not exist: $Measurements"
}
if (-not [string]::IsNullOrWhiteSpace($Calibrations) -and -not (Test-Path -LiteralPath $Calibrations -PathType Leaf)) {
    throw "Calibration list does not exist: $Calibrations"
}

$candidate = Join-Path ([IO.Path]::GetTempPath()) ("CanapeOnlineCandidate_" + [Guid]::NewGuid().ToString('N') + '.a2l')
$backupA2l = $null
try {
    Write-Host '[1/4] 连接 CANape 并读取当前 A2L...'
    $savedErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $probeOutput = @(& $probeExe $Project $probeScript ('--module=' + $Module) 2>&1)
        $probeExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedErrorActionPreference
    }
    if ($probeExitCode -ne 0) {
        throw "阶段=连接/读取当前A2L失败，退出码=$probeExitCode`r`n$($probeOutput -join [Environment]::NewLine)"
    }

    $databaseDirectoryLine = $probeOutput | Where-Object { $_ -like 'DatabaseDirectory.*' } | Select-Object -First 1
    $databaseFileLine = $probeOutput | Where-Object { $_ -like 'DatabaseFile.*' } | Select-Object -First 1
    if ($null -eq $databaseDirectoryLine -or $null -eq $databaseFileLine) {
        throw "The active A2L path was not returned by CANape.`r`n$($probeOutput -join [Environment]::NewLine)"
    }

    $databaseDirectory = ($databaseDirectoryLine -split '=', 2)[1]
    $databaseFile = ($databaseFileLine -split '=', 2)[1]
    $activeA2l = Join-Path $databaseDirectory $databaseFile

    $coreArguments = @('--a2l', $activeA2l, '--elf', $Elf, '--out', $candidate)
    if (-not [string]::IsNullOrWhiteSpace($Measurements)) {
        $coreArguments += @('--signals', $Measurements)
    }
    if (-not [string]::IsNullOrWhiteSpace($Calibrations)) {
        $coreArguments += @('--calibrations', $Calibrations)
    }

    $savedErrorActionPreference = $ErrorActionPreference
    try {
        # ASAP2 Studio writes informational messages to stderr. Do not treat those messages as PowerShell failures.
        $ErrorActionPreference = 'Continue'
        $coreOutput = @(& $coreExe @coreArguments 2>&1)
        $coreExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedErrorActionPreference
    }
    $coreOutput | ForEach-Object { Write-Host $_ }
    $notFoundNames = @()
    $failedNames = @()
    foreach ($line in $coreOutput) {
        $resultLine = "$line"
        if ($resultLine -match '^RESULT\|(?:measurement|calibration)\|not-found\|') {
            $notFoundNames += ($resultLine -split '\|', 4)[3]
        }
        elseif ($resultLine -match '^RESULT\|(?:measurement|calibration)\|failed\|') {
            $failedNames += ($resultLine -split '\|', 4)[3]
        }
    }

    if (($coreExitCode -ne 0) -and -not (($coreExitCode -eq 3) -and (Test-Path -LiteralPath $candidate -PathType Leaf))) {
        throw "阶段=生成候选A2L失败，退出码=$coreExitCode`r`n$($coreOutput -join [Environment]::NewLine)"
    }
    if ($coreExitCode -eq 3) {
        $partialCount = $notFoundNames.Count + $failedNames.Count
        Write-Warning ("Candidate A2L generated with {0} object(s) not processed; continuing with successful objects." -f $partialCount)
    }

    Write-Host '[2/4] 已生成候选 A2L，开始在线刷新 CANape...'
    $applyOutput = @()
    $applyExitCode = 1
    for ($applyAttempt = 1; $applyAttempt -le 3; $applyAttempt++) {
        if ($applyAttempt -gt 1) { Start-Sleep -Seconds 2 }
        $savedErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $applyOutput = @(& $applyExe $Project $Module $candidate 2>&1)
            $applyExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $savedErrorActionPreference
        }
        $applyText = $applyOutput -join [Environment]::NewLine
        if ($applyExitCode -eq 0) { break }
        if ($applyText -notmatch 'error=54|ASAP3 connection failed') { break }
        Write-Warning ("ASAP3 online refresh connection failed (attempt {0}/3); retrying..." -f $applyAttempt)
    }
    $applyOutput | ForEach-Object { Write-Host $_ }
    $backupLine = $applyOutput | Where-Object { "$_" -like 'BackupA2L=*' } | Select-Object -First 1
    if ($null -ne $backupLine) { $backupA2l = ("$backupLine" -split '=', 2)[1] }
    if ($applyExitCode -ne 0) {
        throw "阶段=在线刷新A2L失败，退出码=$applyExitCode`r`n$($applyOutput -join [Environment]::NewLine)"
    }

    $objectNames = @()
    foreach ($listPath in @($Measurements, $Calibrations)) {
        if (-not [string]::IsNullOrWhiteSpace($listPath)) {
            $objectNames += Get-Content -LiteralPath $listPath -Encoding UTF8 |
                ForEach-Object { $_.Trim() } |
                Where-Object { $_.Length -gt 0 -and -not $_.StartsWith('#') }
        }
    }
    $objectNames = $objectNames | Select-Object -Unique
    $skipVerification = @($notFoundNames + $failedNames) | Select-Object -Unique
    if (@($skipVerification).Count -gt 0) {
        $objectNames = @($objectNames | Where-Object { $_ -notin $skipVerification })
        Write-Host ("Skipped verification objects: {0}" -f ($skipVerification -join ', '))
    }
    if (@($objectNames).Count -eq 0) {
        throw 'No successfully processed objects are available for verification. Check whether the ELF matches the CANape software.'
    }

    $diskMissingNames = @()
    try {
        # ASAP3 may still expose stale/unknown objects after a reload. Verify the actual active A2L too.
        $activeA2lText = [IO.File]::ReadAllText($activeA2l)
        foreach ($name in $objectNames) {
            if ($activeA2lText.IndexOf($name, [StringComparison]::Ordinal) -lt 0) {
                $diskMissingNames += $name
            }
        }
    }
    catch {
        $readError = $_.Exception.Message
        throw ("Stage=active A2L file verification failed: {0}`r`n{1}" -f $activeA2l, $readError)
    }
    if (@($diskMissingNames).Count -gt 0) {
        throw ("Stage=active A2L content is incomplete. Missing objects: {0}`r`nThe operation is not considered successful." -f ($diskMissingNames -join ', '))
    }
    Write-Host '[3/4] Online refresh completed. Waiting for CANape object index...'
    $verified = $false
    $verifyOutput = @()
    $maxVerificationAttempts = 10
    for ($attempt = 1; $attempt -le $maxVerificationAttempts; $attempt++) {
        # ReplaceDatabase may restart CANape's measurement service before ASAP3 accepts a new client.
        Start-Sleep -Milliseconds 2000
        $savedErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $probeObjects = @()
            if (-not [string]::IsNullOrWhiteSpace($Measurements)) {
                $probeObjects += (Get-Content -LiteralPath $Measurements -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 -and -not $_.StartsWith('#') } | ForEach-Object { 'M:' + $_ })
            }
            if (-not [string]::IsNullOrWhiteSpace($Calibrations)) {
                $probeObjects += (Get-Content -LiteralPath $Calibrations -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 -and -not $_.StartsWith('#') } | ForEach-Object { 'C:' + $_ })
            }
            $probeArguments = @($Project, $probeScript, ('--module=' + $Module)) + $probeObjects
            $verifyOutput = @(& $probeExe $probeArguments 2>&1)
            $verifyExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $savedErrorActionPreference
        }
        $verifyOutput | ForEach-Object { Write-Host $_ }
        if ($verifyExitCode -eq 0) {
            $verified = $true
            break
        }
        if (($verifyOutput -join [Environment]::NewLine) -match 'error=54') {
            Write-Host ("CANape/ASAP3 is still restarting after database refresh (attempt {0}/{1}); retrying..." -f $attempt, $maxVerificationAttempts)
        }
        else {
            Write-Host ("Verification attempt {0}/{1} failed; retrying..." -f $attempt, $maxVerificationAttempts)
        }
    }
    if (-not $verified) {
        $verifyDetails = $verifyOutput -join [Environment]::NewLine
        throw ("Stage=online verification failed. The A2L may have been written, but CANape/ASAP3 did not return all objects.`r`n" + $verifyDetails)
    }

    Write-Host '[4/4] Verification passed.'
    Write-Host 'Online measurement/calibration update completed.'
}
catch {
    if (-not [string]::IsNullOrWhiteSpace($backupA2l) -and (Test-Path -LiteralPath $backupA2l -PathType Leaf)) {
        Write-Warning ("Verification failed; attempting automatic rollback from backup: {0}" -f $backupA2l)
        try {
            $rollbackOutput = & $applyExe $Project $Module '--restore' $backupA2l 2>&1
            $rollbackExitCode = $LASTEXITCODE
            $rollbackOutput | ForEach-Object { Write-Host $_ }
            if ($rollbackExitCode -ne 0) { Write-Warning ("Automatic rollback failed, exit code={0}" -f $rollbackExitCode) }
        } catch { Write-Warning ("Automatic rollback failed: {0}" -f $_.Exception.Message) }
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $candidate) {
        Remove-Item -LiteralPath $candidate -Force
    }
}
