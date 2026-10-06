#requires -version 3.0
<#
  package.ps1 —— 把构建产物打成一个可以直接发出去的安装包目录 + zip。

  用法：
    .\tools\package.ps1
    .\tools\package.ps1 -SkipBuild
#>
[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Out  = Join-Path $Root 'out'
$Dist = Join-Path $Root 'dist'
$Name = 'tty'
$Ver  = '1.0.0'
$Pkg  = Join-Path $Dist $Name

if (-not $SkipBuild) {
    Write-Host '==> 构建' -ForegroundColor Cyan
    & (Join-Path $Root 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw '构建失败' }
}

if (Test-Path -LiteralPath $Pkg) { Remove-Item -LiteralPath $Pkg -Recurse -Force }
New-Item -ItemType Directory -Path $Pkg -Force | Out-Null

Write-Host '==> 复制程序文件' -ForegroundColor Cyan
$files = @('MyIme.Core.exe','MyIme.Settings.exe','tty.exe','MyIme.Shared.dll','icon.ico')
foreach ($f in $files) {
    $src = Join-Path $Out $f
    if (-not (Test-Path -LiteralPath $src)) { throw "缺少产物: $f（先跑 build.ps1）" }
    Copy-Item -LiteralPath $src -Destination $Pkg -Force
}

Write-Host '==> 复制词库' -ForegroundColor Cyan
$dataOut = Join-Path $Out 'data'
$dataPkg = Join-Path $Pkg 'data'
New-Item -ItemType Directory -Path $dataPkg -Force | Out-Null
Copy-Item -Path (Join-Path $dataOut '*') -Destination $dataPkg -Recurse -Force

Write-Host '==> 复制说明文档' -ForegroundColor Cyan
foreach ($doc in @('README.md','使用说明.txt','设计文档.md')) {
    $p = Join-Path $Root $doc
    if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination $Pkg -Force }
}

# 给最终用户看的极简说明（放在包里最显眼的位置）
$quick = @'
代龙 —— 快速开始

【安装】
  双击 tty.exe，一路下一步即可。
  默认装到当前用户目录，不需要管理员权限。

  静默安装：tty.exe --silent-install
  卸载：    tty.exe --uninstall

【开始打字】
  装好后托盘会出现绿色「墨」字图标，直接在任何输入框里敲拼音即可，例如：
      nihao      空格上屏 -> 你好
      zhongguo   空格上屏 -> 中国
      shurufa    空格上屏 -> 输入法
  不需要用 Win+Space 切换系统输入法。

【常用热键】
  Ctrl+Alt+E   中英文切换（也可以轻按一下 Shift）
  Ctrl+Alt+L   锁定/解锁输入法（打游戏时按一下，按键就完全不经过输入法了）
  Ctrl+Alt+A   打开 AI 面板（自己写要求）
  Ctrl+Alt+P   打开 AI 面板（自动读取当前输入框并润色）
  Ctrl+Alt+S   打开设置
  空格 上屏首候选    1-9 选候选    - = 或左右方向键 翻页    Esc 取消

【重要：AI 功能默认是关的】
  打开设置 -> AI 写作 -> 打开总开关 -> 选服务商 -> 填 API Key -> 保存。
  支持 DeepSeek / 通义千问 / Kimi / 智谱 / OpenAI / 文心 / 豆包 等一堆服务商，
  也支持 Ollama、LM Studio 这类本地模型（本地模型不用填 Key）。
  API Key 用 Windows 加密存储，不会以明文保存。

【打游戏】
  按 Ctrl+Alt+L 锁定后，输入法会把自己的键盘钩子整个卸载，游戏收到的按键
  和没装输入法时完全一样。也可以在设置 -> 游戏锁定 里打开「全屏自动锁定」。

【出问题】
  托盘图标右键 -> 打开日志文件夹，把日志发出来即可定位。
'@
Set-Content -LiteralPath (Join-Path $Pkg '使用说明.txt') -Value $quick -Encoding UTF8

Write-Host '==> 打包 zip' -ForegroundColor Cyan
$zip = Join-Path $Dist ($Name + '.zip')
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($Pkg, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host ''
Write-Host '  打包完成' -ForegroundColor Green
Write-Host "  目录: $Pkg"
Write-Host "  压缩包: $zip"
Get-ChildItem -LiteralPath $Pkg | Sort-Object Name | ForEach-Object {
    if ($_.PSIsContainer) { Write-Host ('    ' + $_.Name + '/') -ForegroundColor DarkGray }
    else { Write-Host ('    ' + $_.Name.PadRight(24) + ('{0,8:N0} KB' -f ($_.Length / 1KB))) -ForegroundColor DarkGray }
}
$dataCount = (Get-ChildItem -LiteralPath $dataPkg -File | Measure-Object).Count
Write-Host "    data/ ($dataCount 个词库文件)" -ForegroundColor DarkGray
Write-Host ''
