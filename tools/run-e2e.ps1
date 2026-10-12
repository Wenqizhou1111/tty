#requires -version 3.0
<#
  run-e2e.ps1 —— 跑端到端打字测试

  用法：  .\tools\run-e2e.ps1

  测试专用环境变量：
    MYIME_ACCEPT_INJECTED=1  让输入法接受"注入的"按键。
                             平时必须忽略（否则自己上屏的字会被自己再处理一遍），
                             但自动化测试正是靠 SendInput 合成按键驱动的，所以测试时要打开。
    MYIME_DATA_DIR           把设置和用户词库指到仓库内的临时目录，避免污染 %APPDATA%。
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
$Root = Split-Path -Parent $PSScriptRoot
$Out  = Join-Path $Root 'out'
$Tmp  = Join-Path $Root '.e2e'
$Log  = Join-Path $Root '_e2e_result.txt'

if (-not (Test-Path -LiteralPath (Join-Path $Out 'MyIme.Core.exe'))) { throw '先跑 .\build.ps1' }

if (Test-Path -LiteralPath $Tmp) { Remove-Item -LiteralPath $Tmp -Recurse -Force }
New-Item -ItemType Directory -Path $Tmp | Out-Null

$env:MYIME_DATA_DIR        = $Tmp
$env:MYIME_ACCEPT_INJECTED = '1'
$env:MYIME_LOG_LEVEL       = 'info'

$core = Start-Process -FilePath (Join-Path $Out 'MyIme.Core.exe') -PassThru -WorkingDirectory $Out
Start-Sleep -Seconds 3
Write-Host '==> 输入法已启动，开始打字测试' -ForegroundColor Cyan

# 直接同步调用：不要用 -RedirectStandardOutput，那个在 WinForms 子进程上会卡住不返回
$text = & (Join-Path $Out 'TypeTest.exe') 2>&1 | Out-String
Start-Sleep -Milliseconds 400

if (-not $core.HasExited) { $core.Kill() }
Set-Content -LiteralPath $Log -Value $text -Encoding UTF8
Write-Host $text

Remove-Item -LiteralPath $Tmp -Recurse -Force -ErrorAction SilentlyContinue
if ($text -match 'FAIL') { exit 1 }
Write-Host '==> 端到端测试通过' -ForegroundColor Green