# How pare treats Windows device paths (\\?\ and \\.\) as inputs, against plain paths.
# Before the fix of 2026-10-31 the ? of \\?\ counted as a wildcard, so pare walked the whole drive
# (over ssh on wp1 the walk hit a Codex sandbox folder: "untrusted mount point"). Every case now
# writes what its baseline writes.
#
#   pwsh scripts/check-device-paths.ps1     # from the repo root, on dev
#
# Builds the CLI, strips two small files through each path form, and writes
# tmp/device-paths-<stamp>/report.txt. Send that file back. Deletes nothing.

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = [IO.Path]::GetFullPath((Join-Path $root "tmp\device-paths-$stamp"))
$report = Join-Path $work 'report.txt'
New-Item -ItemType Directory -Force (Join-Path $work 'src\sub') | Out-Null
$a = Join-Path $work 'src\a.ts'
$b = Join-Path $work 'src\sub\b.ts'
[IO.File]::WriteAllText($a, "let a: number = 1;`n")
[IO.File]::WriteAllText($b, "let b: string = 'x';`n")

$lines = [Collections.Generic.List[string]]::new()
function Say([string]$s) { $lines.Add($s); Write-Host $s }

dotnet build (Join-Path $root 'src\Extrode.Pare.Cli') -c Debug -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
$pare = Join-Path $root 'src\Extrode.Pare.Cli\bin\Debug\net10.0\pare.exe'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
$long = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Name LongPathsEnabled -ErrorAction SilentlyContinue).LongPathsEnabled
Say "pare device path check $stamp"
Say "commit:   $(git -C $root rev-parse --short HEAD) ($(git -C $root branch --show-current))"
Say "os:       $([Environment]::OSVersion.VersionString), pwsh $($PSVersionTable.PSVersion)"
Say "session:  $(if ($env:SSH_CONNECTION) { 'ssh' } else { 'interactive' }), admin=$admin, LongPathsEnabled=$long"
Say "work:     $work"
Say ''

# Does the OS (through .NET) accept the device forms at all in this session?
Say '== OS'
foreach ($p in @("\\?\$a", "\\.\$a", "\\?\$work\src")) {
    try {
        $kind = if ([IO.File]::Exists($p)) { 'file' } elseif ([IO.Directory]::Exists($p)) { 'directory' } else { 'not found' }
        $list = if ($kind -eq 'directory') { ' entries=' + ([IO.Directory]::GetFileSystemEntries($p).Count) } else { '' }
        Say "$p -> $kind$list"
    } catch { Say "$p -> $($_.Exception.GetType().Name): $($_.Exception.Message)" }
}
# A child process, like pare: over ssh on wp1 (2026-10-31) pare's \\?\ inputs failed with
# "untrusted mount point" naming a Codex sandbox folder, while the checks above passed.
$child = pwsh -NoProfile -Command "try { [IO.File]::ReadAllText('\\?\$a').Length } catch { `$_.Exception.Message }"
Say "child process reads \\?\ file -> $child"
Say "process cwd: $([Environment]::CurrentDirectory)"
$subst = (subst) -join '; '
Say "subst: $(if ($subst) { $subst } else { '(none)' })"
Say ''

$fwd = $work.Replace('\', '/')
$cases = [ordered]@{
    'baseline file'      = "$work\src\a.ts"
    'baseline dir'       = "$work\src"
    'baseline *.ts'      = "$work\src\*.ts"
    'baseline **'        = "$work\src\**\*.ts"
    '\\?\ file'          = "\\?\$work\src\a.ts"
    '\\?\ dir'           = "\\?\$work\src"
    '\\?\ *.ts'          = "\\?\$work\src\*.ts"
    '\\?\ **'            = "\\?\$work\src\**\*.ts"
    '//?/ *.ts'          = "//?/$fwd/src/*.ts"
    '\\.\ file'          = "\\.\$work\src\a.ts"
    '\\.\ *.ts'          = "\\.\$work\src\*.ts"
}

Say '== pare (exit, written .js files, output)'
foreach ($name in $cases.Keys) {
    Get-ChildItem -LiteralPath (Join-Path $work 'src') -Recurse -Filter *.js | Remove-Item
    $arg = $cases[$name]
    $out = & $pare $arg 2>&1 | ForEach-Object { "$_" }
    $exit = $LASTEXITCODE
    $written = (Get-ChildItem -LiteralPath (Join-Path $work 'src') -Recurse -Filter *.js |
        ForEach-Object { $_.FullName.Substring($work.Length + 1) }) -join ', '
    if ($written -eq '') { $written = '(none)' }
    Say "[$name] $arg"
    Say "  exit=$exit written=$written"
    foreach ($l in $out) { Say "  | $l" }
}

[IO.File]::WriteAllLines($report, $lines)
Write-Host ''
Write-Host "report: $report"
