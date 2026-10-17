# Windows half of scripts/pack-cli.sh: packs the pare CLI for win-x64, and win-arm64 when Visual
# Studio has the ARM64 C++ tools, into tmp/dist:
#   Extrode.Pare.Cli.<rid>.<v>.nupkg    NativeAOT binary for one RID
#   pare-<v>-<rid>.zip                  the same binary as a standalone archive
#   libpare-<v>-<rid>.zip               the C library: pare.dll, pare.lib, pare.h
#                                       (checked by tests/Extrode.Pare.Integration NativeLibraryTests)
# Then runs the host RID's binary 20 times and `dotnet tool install`s it from a pointer package.
# -SkipPack only runs that check, on archives already in tmp/dist: for a machine without Visual
# Studio, such as a Windows on ARM test machine given win-arm64 packed elsewhere.
# Usage: pwsh scripts/pack-cli.ps1 [-Version 0.4.0] [-SkipPack]
param([string]$Version, [switch]$SkipPack)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'tmp/dist'
if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
New-Item -ItemType Directory -Force $out | Out-Null

function Invoke-Pack([string[]]$Arguments) {
    dotnet pack (Join-Path $root 'src/Extrode.Pare.Cli') -c Release -o $out "-p:Version=$Version" -v q -nologo @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack $Arguments failed" }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$rids = @('win-x64')
if ($SkipPack) {
    $rids = @('win-x64', 'win-arm64') | Where-Object { Test-Path (Join-Path $out "pare-$Version-$_.zip") }
} elseif ((Test-Path $vswhere) -and (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property installationPath)) {
    $rids += 'win-arm64'
} else {
    Write-Warning 'win-arm64 skipped: Visual Studio has no ARM64 C++ tools (Microsoft.VisualStudio.Component.VC.Tools.ARM64)'
}

foreach ($rid in $rids) {
    if ($SkipPack) { continue }
    Invoke-Pack @('-r', $rid)
    $stage = Join-Path $root "tmp/dist-stage/pare-$Version-$rid"
    Remove-Item -Recurse -Force (Join-Path $root 'tmp/dist-stage') -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item (Join-Path $root "src/Extrode.Pare.Cli/bin/Release/net10.0/$rid/publish/pare.exe"), (Join-Path $root 'LICENSE.md'), (Join-Path $root 'NOTICE.md'), (Join-Path $root 'README.md') $stage
    $zip = Join-Path $out "pare-$Version-$rid.zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path $stage -DestinationPath $zip
    Remove-Item -Recurse -Force (Join-Path $root 'tmp/dist-stage')
    "pare-$Version-$rid.zip"

    # libpare: publish copies only pare.dll; the import library stays beside the link output.
    dotnet publish (Join-Path $root 'src/Extrode.Pare.Native') -c Release -r $rid "-p:Version=$Version" -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish libpare $rid failed" }
    $native = Join-Path $root "src/Extrode.Pare.Native"
    $stage = Join-Path $root "tmp/native-stage/libpare-$Version-$rid"  # overwritten, left for scripts/cleanup
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item (Join-Path $native "bin/Release/net10.0/$rid/publish/pare.dll"), (Join-Path $native "obj/Release/net10.0/$rid/native/pare.lib"), (Join-Path $native 'include/pare.h'), (Join-Path $root 'LICENSE.md'), (Join-Path $root 'NOTICE.md') $stage
    Compress-Archive -Path $stage -DestinationPath (Join-Path $out "libpare-$Version-$rid.zip") -Force
    "libpare-$Version-$rid.zip"
}

# The host RID: the archive's binary 20 times, then `dotnet tool install`, which must pick it.
$hostRid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
if ($rids -contains $hostRid) {
    $check = Join-Path $root "tmp/dist-check"
    Remove-Item -Recurse -Force $check -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $check | Out-Null
    Expand-Archive (Join-Path $out "pare-$Version-$hostRid.zip") $check
    $exe = Join-Path $check "pare-$Version-$hostRid/pare.exe"
    [IO.File]::WriteAllText("$check/in.ts", "type T = 1;`nlet x: number = 1;`n")
    $want = "           `nlet x         = 1;`n"
    foreach ($i in 1..20) {
        cmd /c "`"$exe`" --stdin < `"$check\in.ts`" > `"$check\got.js`""
        if ($LASTEXITCODE -ne 0 -or [IO.File]::ReadAllText("$check/got.js") -ne $want) { throw "pare.exe run $i gave a wrong result" }
    }

    # The pointer package lists every RID; a source with only this one is enough to install it.
    # The SDK puts a pare.cmd shim on the tool path for a native tool.
    $feed = Join-Path $check 'feed'
    New-Item -ItemType Directory -Force $feed | Out-Null
    Copy-Item (Join-Path $out "Extrode.Pare.Cli.$hostRid.$Version.nupkg") $feed
    dotnet pack (Join-Path $root 'src/Extrode.Pare.Cli') -c Release -o $feed "-p:Version=$Version" -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet pack (pointer) failed' }
    $env:NUGET_PACKAGES = Join-Path $check 'packages'
    dotnet tool install Extrode.Pare.Cli --version $Version --tool-path (Join-Path $check 't') --add-source $feed | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool install failed' }
    if (-not (Test-Path (Join-Path $check "t/.store/extrode.pare.cli/$Version/extrode.pare.cli.$hostRid"))) { throw "tool install did not pick $hostRid" }
    cmd /c "`"$check\t\pare.cmd`" --stdin < `"$check\in.ts`" > `"$check\got.js`""
    if ([IO.File]::ReadAllText("$check/got.js") -ne $want) { throw 'installed tool gave a wrong result' }
    Remove-Item Env:NUGET_PACKAGES
    Remove-Item -Recurse -Force $check
    "verified $hostRid"
} elseif ($SkipPack) {
    throw "no pare-$Version-$hostRid.zip in tmp/dist"
}
if (-not $SkipPack) {
    "packed Extrode.Pare.Cli $Version for $($rids -join ', ') into tmp/dist"
}
