#requires -version 3.0
<#
  build.ps1 —— MyIME 构建脚本（不需要 Visual Studio / Windows SDK / .NET SDK）

  用 Windows 自带的 .NET Framework 4.x 编译器 csc.exe 直接编译。
  Windows 10 / 11 一定自带它，所以任何一台干净的机器都能构建。

  ！！随系统自带的 csc.exe 是 **C# 5** 编译器！！
  源码里不能使用：字符串内插 $"..."、?. 空传播、nameof、
  表达式体成员 =>、自动属性初始化器、using static、元组、模式匹配。

  用法：
    .\build.ps1              构建全部产物到 out\
    .\build.ps1 -Tests       构建后运行引擎单元测试
    .\build.ps1 -Clean       先清空 out\
#>
[CmdletBinding()]
param(
    [switch]$Tests,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
if (-not $Root) { $Root = (Get-Location).Path }
$SrcDir  = Join-Path $Root 'src'
$DataDir = Join-Path $Root 'data'
$OutDir  = Join-Path $Root 'out'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "    $msg" -ForegroundColor DarkGray }

# ---------------------------------------------------------------- 找编译器
function Find-Csc {
    $candidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return $c } }
    $pf86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    $globs = @(
        "$env:ProgramFiles\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe",
        "$pf86\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe",
        "$env:ProgramFiles\dotnet\sdk\*\Roslyn\bincore\csc.exe"
    )
    foreach ($g in $globs) {
        $hit = Get-ChildItem -Path $g -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    throw "找不到 csc.exe。Windows 10/11 应自带 Framework64\v4.0.30319\csc.exe"
}

$Csc = Find-Csc
$FxDir  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$WpfDir = Join-Path $FxDir 'WPF'
if (-not (Test-Path -LiteralPath $WpfDir)) {
    $WpfDir = Join-Path (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319') 'WPF'
}

function Q([string]$p) { if ($p -match '\s') { return '"' + $p + '"' } else { return $p } }

$CoreRefs = @(
    (Q (Join-Path $FxDir 'System.dll'))
    (Q (Join-Path $FxDir 'System.Core.dll'))
    (Q (Join-Path $FxDir 'System.Drawing.dll'))
    (Q (Join-Path $FxDir 'System.Windows.Forms.dll'))
    (Q (Join-Path $FxDir 'System.Net.Http.dll'))
    (Q (Join-Path $FxDir 'System.Security.dll'))
    (Q (Join-Path $FxDir 'System.Xml.dll'))
    (Q (Join-Path $WpfDir 'UIAutomationClient.dll'))
    (Q (Join-Path $WpfDir 'UIAutomationTypes.dll'))
    (Q (Join-Path $WpfDir 'WindowsBase.dll'))
)

# ---------------------------------------------------------------- 准备目录
if ($Clean -and (Test-Path -LiteralPath $OutDir)) {
    Write-Step '清理 out\'
    Remove-Item -LiteralPath $OutDir -Recurse -Force
}
if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

# /nowarn:
#   0114 / 0108 —— 设置界面里一批自绘控件各自声明了同名的 ApplyTheme()，
#                  属于有意的成员隐藏（调用点都持有具体类型），不是 bug，
#                  但每个控件都刷一条警告会把真正的错误淹没。
$BaseArgs = @('/nologo', '/codepage:65001', '/utf8output', '/warn:4', '/langversion:5', '/nowarn:0114', '/nowarn:0108')

function Invoke-Csc {
    param(
        [string]$Target,
        [string]$OutFile,
        [string[]]$Sources,
        [string[]]$Refs = @()
    )
    $cscArgs = @()
    $cscArgs += $BaseArgs
    $cscArgs += ('/target:' + $Target)
    $cscArgs += ('/out:' + (Q $OutFile))
    $cscArgs += '/platform:anycpu'
    foreach ($r in $Refs)   { $cscArgs += ('/r:' + $r) }
    foreach ($s in $Sources){ $cscArgs += (Q $s) }

    $log = & $Csc @cscArgs 2>&1
    $code = $LASTEXITCODE
    foreach ($line in $log) {
        if ($line -match 'error|warning') {
            $color = 'Yellow'; if ($line -match ': error') { $color = 'Red' }
            Write-Host "    $line" -ForegroundColor $color
        }
    }
    if ($code -ne 0) { throw "编译失败: $OutFile (exit $code)" }
}

function Get-Sources([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    return @(Get-ChildItem -LiteralPath $dir -Filter '*.cs' -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName })
}

Write-Host ''
Write-Host '  MyIME 构建 (csc / .NET Framework)' -ForegroundColor White
Write-Ok "编译器 : $Csc"
Write-Ok "输出   : $OutDir"
Write-Host ''

# ---------------------------------------------------------------- 编译
$SharedOut = Join-Path $OutDir 'MyIme.Shared.dll'
$SharedSrc = Get-Sources (Join-Path $SrcDir 'Shared')
if ($SharedSrc.Count -eq 0) { throw "src\Shared 下没有 .cs 文件" }
Write-Step "MyIme.Shared.dll  ($($SharedSrc.Count) 源文件)"
Invoke-Csc -Target 'library' -OutFile $SharedOut -Sources $SharedSrc
Write-Ok ("{0:N0} KB" -f ((Get-Item -LiteralPath $SharedOut).Length / 1KB))

$SharedRef = Q $SharedOut

$proj = @(
    @{ Name = 'MyIme.Core';     Dir = 'Core';     Target = 'winexe' },
    @{ Name = 'MyIme.Settings'; Dir = 'Settings'; Target = 'winexe' },
    # 安装程序对外的文件名是 tty.exe（用户要的安装包名就是这个）
    @{ Name = 'MyIme.Setup';    Dir = 'Setup';    Target = 'winexe'; Out = 'tty' },
    @{ Name = 'EngineTests';    Dir = 'Tests';    Target = 'exe'    },
    @{ Name = 'TypeTest';       Dir = 'E2E';      Target = 'exe'    }
)
foreach ($p in $proj) {
    $srcs = Get-Sources (Join-Path $SrcDir $p.Dir)
    if ($srcs.Count -eq 0) { Write-Step "$($p.Name) 跳过（无源文件）"; continue }
    $outName = $p.Name
    if ($p.ContainsKey('Out')) { $outName = $p.Out }
    Write-Step "$outName.exe  ($($srcs.Count) 源文件)"
    Invoke-Csc -Target $p.Target -OutFile (Join-Path $OutDir ($outName + '.exe')) -Sources $srcs -Refs ($CoreRefs + $SharedRef)
}

# ---------------------------------------------------------------- 数据
Write-Step '复制数据文件'
$OutData = Join-Path $OutDir 'data'
if (-not (Test-Path -LiteralPath $OutData)) { New-Item -ItemType Directory -Path $OutData | Out-Null }
$copied = 0
foreach ($ext in @('*.txt','*.json')) {
    Get-ChildItem -LiteralPath $DataDir -Filter $ext -File -ErrorAction SilentlyContinue | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $OutData -Force
        $copied++
    }
}
Write-Ok "$copied 个数据文件 -> out\data"

Write-Host ''
Write-Host '  构建完成' -ForegroundColor Green
Get-ChildItem -LiteralPath $OutDir -File | Sort-Object Name | ForEach-Object {
    Write-Host ('    ' + $_.Name.PadRight(22) + ('{0,8:N0} KB' -f ($_.Length / 1KB))) -ForegroundColor DarkGray
}
Write-Host ''

if ($Tests) {
    Write-Step '运行引擎单元测试'
    & (Join-Path $OutDir 'EngineTests.exe')
    if ($LASTEXITCODE -ne 0) { throw "单元测试失败 (exit $LASTEXITCODE)" }
}
