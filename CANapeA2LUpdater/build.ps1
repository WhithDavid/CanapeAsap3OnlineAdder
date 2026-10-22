$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$requiredVectorFiles = @(
    'Vector.MCD.ASAP2.DataModel.dll',
    'Vector.MCD.ASAP2.Reader.dll',
    'Vector.MCD.ASAP2.Writer.dll',
    'Vector.MCD.ASAP2.Updater.dll',
    'Vector.MCD.LinkMap.Contract.dll',
    'Vector.MCD.LinkMap.DataModel.dll',
    'Vector.MCD.LinkMap.SymbolsProvider.dll'
)
$vectorBin = $env:ASAP2_STUDIO_BIN
if (-not [string]::IsNullOrWhiteSpace($vectorBin)) {
    $missing = $requiredVectorFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $vectorBin $_) -PathType Leaf) }
    if ($missing.Count -gt 0) { $vectorBin = $null }
}
if ([string]::IsNullOrWhiteSpace($vectorBin)) {
    $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { Join-Path $_ 'Vector ASAP2 Studio' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container }
    $vectorBin = $roots |
        ForEach-Object { Get-ChildItem -LiteralPath $_ -Directory -Filter 'ASAP2StudioCANape*' -ErrorAction SilentlyContinue } |
        ForEach-Object { Join-Path $_.FullName 'Bin' } |
        Where-Object {
            $candidate = $_
            -not ($requiredVectorFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $candidate $_) -PathType Leaf) })
        } |
        Sort-Object -Descending |
        Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($vectorBin)) {
    throw 'ASAP2 Studio API not found. Set ASAP2_STUDIO_BIN to the Vector ASAP2 Studio Bin directory.'
}
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputDir = Join-Path $projectDir 'bin'
$releaseDir = Join-Path $projectDir 'Release'
$coreOutput = Join-Path $outputDir 'CanapeA2LUpdater.Core.dll'
$bootstrapOutput = Join-Path $outputDir 'CanapeA2LUpdater.exe'
$guiOutput = Join-Path $outputDir 'QTAutoAdder.exe'
$probeOutput = Join-Path $outputDir 'Asap3ReadOnlyProbe.exe'
$statusProbeOutput = Join-Path $outputDir 'CanapeReadOnlyProbe.exe'
$onlineApplyOutput = Join-Path $outputDir 'CanapeOnlineApply.exe'
$onlineScriptOutput = Join-Path $outputDir 'OnlineUpdate.ps1'
$probeScriptOutput = Join-Path $outputDir 'probe_script_channel.cns'
$iconOutput = Join-Path $outputDir 'qt-auto-adder.ico'
$runtimeDiscovery = Join-Path $projectDir 'RuntimeDiscovery.cs'

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$references = $requiredVectorFiles | ForEach-Object { '/reference:' + (Join-Path $vectorBin $_) }

& $compiler /nologo /target:library /platform:x64 "/out:$coreOutput" $references (Join-Path $projectDir 'Core.cs')
if ($LASTEXITCODE -ne 0) { throw 'Core compilation failed.' }

& $compiler /nologo /target:exe /platform:x64 "/out:$bootstrapOutput" (Join-Path $projectDir 'Bootstrap.cs') $runtimeDiscovery
if ($LASTEXITCODE -ne 0) { throw 'Bootstrap compilation failed.' }

& $compiler /nologo /target:exe /platform:x64 "/out:$projectDir\bin\MakeIcon.exe" /reference:System.Drawing.dll (Join-Path $projectDir 'Tools\MakeIcon.cs')
if ($LASTEXITCODE -ne 0) { throw 'Icon generator compilation failed.' }
& "$projectDir\bin\MakeIcon.exe" $iconOutput
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }

& $compiler /nologo /target:winexe /platform:x64 "/out:$guiOutput" "/win32icon:$iconOutput" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll (Join-Path $projectDir 'GuiBootstrap.cs') $runtimeDiscovery
if ($LASTEXITCODE -ne 0) { throw 'GUI compilation failed.' }

& $compiler /nologo /target:exe /platform:x64 "/out:$probeOutput" (Join-Path $projectDir 'Tools\Asap3ReadOnlyProbe.cs') $runtimeDiscovery
if ($LASTEXITCODE -ne 0) { throw 'ASAP3 probe compilation failed.' }

& $compiler /nologo /target:exe /platform:x64 "/out:$statusProbeOutput" (Join-Path $projectDir 'Tools\CanapeReadOnlyProbe.cs') $runtimeDiscovery
if ($LASTEXITCODE -ne 0) { throw 'CANape status probe compilation failed.' }

& $compiler /nologo /target:exe /platform:x64 "/out:$onlineApplyOutput" (Join-Path $projectDir 'Tools\CanapeOnlineApply.cs') $runtimeDiscovery
if ($LASTEXITCODE -ne 0) { throw 'Online apply compilation failed.' }

Copy-Item -LiteralPath (Join-Path $projectDir 'OnlineUpdate.ps1') -Destination $onlineScriptOutput -Force
Copy-Item -LiteralPath (Join-Path $projectDir 'probe_script_channel.cns') -Destination $probeScriptOutput -Force

# Keep the user-facing Release directory synchronized with the verified build.
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
foreach ($artifact in @(
    'QTAutoAdder.exe',
    'CanapeA2LUpdater.exe',
    'CanapeOnlineApply.exe',
    'Asap3ReadOnlyProbe.exe',
    'CanapeReadOnlyProbe.exe',
    'CanapeA2LUpdater.Core.dll',
    'OnlineUpdate.ps1',
    'probe_script_channel.cns',
    'qt-auto-adder.ico'
)) {
    $source = Join-Path $outputDir $artifact
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Release artifact does not exist: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $releaseDir $artifact) -Force
}
Copy-Item -LiteralPath (Join-Path $projectDir 'README.md') -Destination (Join-Path $releaseDir '使用说明.md') -Force

Write-Host "Build complete: $outputDir"
