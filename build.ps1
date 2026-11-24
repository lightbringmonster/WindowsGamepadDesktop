param([string]$OutDir = (Join-Path $PSScriptRoot 'bin'), [switch]$Test)
$ErrorActionPreference = 'Stop'
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $fx 'csc.exe'
[void][IO.Directory]::CreateDirectory($OutDir)
$source = @(Get-ChildItem (Join-Path $PSScriptRoot src) -Recurse -Filter *.cs | ForEach-Object FullName)
$flags = @('/nologo','/utf8output','/codepage:65001','/platform:x64','/optimize+','/warn:4','/warnaserror+','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll')
foreach ($lib in 'UIAutomationClient','UIAutomationTypes','WindowsBase') { $flags += "/r:$fx\WPF\$lib.dll" }
$exe = Join-Path $OutDir 'WindowsGamepadDesktop.exe'
& $compiler @flags /target:winexe "/win32icon:$PSScriptRoot\gamepad.ico" "/win32manifest:$PSScriptRoot\app.manifest" "/out:$exe" @source
if ($LASTEXITCODE) { throw 'Application build failed' }
$data = Join-Path $PSScriptRoot 'data'
if (Test-Path $data) { Copy-Item $data $OutDir -Recurse -Force }
if ($Test) {
    $tests = @(Get-ChildItem (Join-Path $PSScriptRoot tests) -Filter *.cs | ForEach-Object FullName)
    $runner = Join-Path $OutDir 'Tests.exe'
    & $compiler @flags "/win32manifest:$PSScriptRoot\app.manifest" /target:exe /r:Microsoft.CSharp.dll /main:Gamepad.Tests "/out:$runner" @source @tests
    if ($LASTEXITCODE) { throw 'Test build failed' }
    & $runner
    if ($LASTEXITCODE) { throw 'Tests failed' }
}