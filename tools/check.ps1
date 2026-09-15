#requires -version 3.0
<#
  check.ps1 —— 只编译某个子目录做语法检查，输出到临时文件，不碰 out\。
  给并行开发用：各人只管自己的目录，互不干扰。

  用法：
    .\tools\check.ps1 -Dir src\Core\Ai
    .\tools\check.ps1 -Dir src\Settings
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Dir,
    [string]$Out = ''
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$fx  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $fx 'WPF'
$csc = Join-Path $fx 'csc.exe'
$shared = Join-Path $Root 'out\MyIme.Shared.dll'

if (-not (Test-Path -LiteralPath $shared)) {
    Write-Host '先跑 .\build.ps1 生成 out\MyIme.Shared.dll' -ForegroundColor Red
    exit 2
}
if ($Out -eq '') {
    $Out = Join-Path $env:TEMP ('myime_check_' + [IO.Path]::GetFileName($Dir) + '.dll')
}

$target = Join-Path $Root $Dir
$srcs = @(Get-ChildItem -LiteralPath $target -Filter '*.cs' -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName | ForEach-Object { $_.FullName })
if ($srcs.Count -eq 0) { Write-Host "没有 .cs 文件: $Dir" -ForegroundColor Yellow; exit 0 }

$refs = @(
    (Join-Path $fx 'System.dll')
    (Join-Path $fx 'System.Core.dll')
    (Join-Path $fx 'System.Drawing.dll')
    (Join-Path $fx 'System.Windows.Forms.dll')
    (Join-Path $fx 'System.Net.Http.dll')
    (Join-Path $fx 'System.Security.dll')
    (Join-Path $fx 'System.Xml.dll')
    (Join-Path $wpf 'UIAutomationClient.dll')
    (Join-Path $wpf 'UIAutomationTypes.dll')
    (Join-Path $wpf 'WindowsBase.dll')
    $shared
)

$a = @('/nologo','/codepage:65001','/utf8output','/warn:4','/langversion:5','/target:library',('/out:' + $Out))
foreach ($r in $refs) { $a += ('/r:' + $r) }
foreach ($s in $srcs) { $a += $s }

$log = & $csc @a 2>&1
$code = $LASTEXITCODE
foreach ($line in $log) {
    $color = 'Yellow'; if ($line -match ': error') { $color = 'Red' }
    Write-Host $line -ForegroundColor $color
}
if ($code -eq 0) {
    Write-Host "编译通过: $Dir  ($($srcs.Count) 个文件) -> $Out" -ForegroundColor Green
    exit 0
}
Write-Host "编译失败: $Dir" -ForegroundColor Red
exit 1
