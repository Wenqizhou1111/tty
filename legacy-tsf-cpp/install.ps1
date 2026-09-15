<#
.SYNOPSIS
    安装 / 卸载 MyIME 输入法（需要管理员权限）。

.DESCRIPTION
    安装做的四件事：
      1. 把 DLL 与词典拷到 %ProgramFiles%\MyIME\{x64,x86,data}
      2. 用对应位数的 regsvr32 调用 DllRegisterServer
         （x64 用 System32\regsvr32，x86 用 SysWOW64\regsvr32 —— 位数反了会写错注册表视图）
      3. 提示怎么在系统里启用（Win+Space 或语言设置）
      4. 卸载时反注册并删目录

    为什么 x86 也要注册：64 位 Windows 上仍有 32 位进程（老应用、某些 Office 插件）。
    只注册 64 位 DLL，在这些进程里输入法是不存在的。

.EXAMPLE
    .\install.ps1 -Action install
    .\install.ps1 -Action status
    .\install.ps1 -Action uninstall
#>
[CmdletBinding()]
param(
    [ValidateSet('install', 'uninstall', 'status')]
    [string]$Action = 'install',
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'MyIME'),
    [string]$SourceDir = (Join-Path $PSScriptRoot 'out')
)

$ErrorActionPreference = 'Stop'

$Clsid = '{D3F1A7C2-5B4E-4A9F-8C61-7E2D4B9A3F10}'   # 必须与 src/Guids.cpp 一致
$ProfileGuid = '{7C4E1D92-3A8B-4F6E-B5D2-91C0A7E4F3B8}'
$LangKey = '0x00000804'                              # zh-CN

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Admin {
    if (-not (Test-Admin)) {
        throw '需要管理员权限：请用「以管理员身份运行」打开 PowerShell 再执行本脚本。'
    }
}

function Invoke-RegSvr32([string]$dllPath) {
    $is64 = [Environment]::Is64BitOperatingSystem
    $view = if ($dllPath -match '\\x86\\') { 'x86' } else { 'x64' }

    if ($view -eq 'x86') {
        $regsvr32 = Join-Path $env:SystemRoot 'SysWOW64\regsvr32.exe'
    } else {
        $regsvr32 = Join-Path $env:SystemRoot 'System32\regsvr32.exe'
    }

    $argument = if ($script:Unregistering) { '/u /s' } else { '/s' }
    Write-Host "  regsvr32($view) $argument `"$dllPath`"" -ForegroundColor DarkGray
    $process = Start-Process -FilePath $regsvr32 -ArgumentList "$argument `"$dllPath`"" -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        Write-Warning "regsvr32 返回 $($process.ExitCode)（0 表示成功；常见失败原因：没装 VC++ 运行库 / DLL 依赖缺失）"
    }
    return $process.ExitCode
}

function Show-Status {
    Write-Host "`n--- 注册表检查 ---" -ForegroundColor Cyan

    $tipKey = "HKLM:\SOFTWARE\Microsoft\CTF\TIP\$Clsid"
    $comKey = "HKLM:\SOFTWARE\Classes\CLSID\$Clsid"
    $tipeKey = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\CTF\TIP\$Clsid"

    foreach ($item in @(
        @{ Name = 'x64 TSF 文本服务'; Path = $tipKey },
        @{ Name = 'x64 COM 注册'; Path = $comKey },
        @{ Name = 'x86 TSF 文本服务'; Path = $tipeKey }
    )) {
        if (Test-Path $item.Path) {
            Write-Host "  [有] $($item.Name): $($item.Path)" -ForegroundColor Green
            $key = Get-Item $item.Path
            foreach ($valueName in $key.GetValueNames()) {
                $display = if ($valueName -eq '') { '(默认)' } else { $valueName }
                Write-Host "        $display = $($key.GetValue($valueName))"
            }
            foreach ($sub in $key.GetSubKeyNames()) { Write-Host "        子键: $sub" }
        } else {
            Write-Host "  [无] $($item.Name)" -ForegroundColor DarkGray
        }
    }

    $profileKey = "$tipKey\LanguageProfile\$LangKey\$ProfileGuid"
    if (Test-Path $profileKey) {
        Write-Host "  [有] 语言档案: $profileKey" -ForegroundColor Green
    } else {
        Write-Host "  [无] 语言档案" -ForegroundColor DarkGray
    }

    Write-Host "`n--- 已装文件 ---" -ForegroundColor Cyan
    if (Test-Path $InstallDir) {
        Get-ChildItem $InstallDir -Recurse | Select-Object FullName, Length | Format-Table -AutoSize
    } else {
        Write-Host "  目录不存在: $InstallDir" -ForegroundColor DarkGray
    }

    Write-Host "`n--- 当前可见的输入法（语言列表）---" -ForegroundColor Cyan
    try {
        Add-Type -AssemblyName PresentationFramework -ErrorAction SilentlyContinue
        $profiles = [Windows.Input.InputLanguageManager]::Current.InputLanguageLists
        foreach ($list in $profiles) {
            foreach ($lang in $list) {
                foreach ($method in $lang.InputMethods()) {
                    Write-Host "  $($lang.LanguageName): $($method.Description)  [$($method.Id)]"
                }
            }
        }
    } catch {
        Write-Host "  (用 .NET 枚举失败，可改用「设置 → 时间和语言 → 语言和区域 → 中文 → 语言选项」查看)" -ForegroundColor DarkGray
    }
}

switch ($Action) {
    'status' {
        Show-Status
    }

    'install' {
        Assert-Admin

        $x64 = Join-Path $SourceDir 'x64\MyIme.dll'
        $x86 = Join-Path $SourceDir 'x86\MyIme.dll'
        if (-not (Test-Path $x64) -and -not (Test-Path $x86)) {
            throw "没有找到编译产物（$x64）。先执行 .\build.ps1"
        }

        Write-Host "`n[1/4] 复制文件到 $InstallDir" -ForegroundColor Cyan
        New-Item -ItemType Directory -Force -Path (Join-Path $InstallDir 'data') | Out-Null
        if (Test-Path $x64) {
            New-Item -ItemType Directory -Force -Path (Join-Path $InstallDir 'x64') | Out-Null
            Copy-Item $x64 (Join-Path $InstallDir 'x64') -Force
            # DLL 会先找同目录的 dictionary.txt，再找上级的 data\dictionary.txt
            Copy-Item (Join-Path $SourceDir 'dictionary.txt') (Join-Path $InstallDir 'x64') -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path $x86) {
            New-Item -ItemType Directory -Force -Path (Join-Path $InstallDir 'x86') | Out-Null
            Copy-Item $x86 (Join-Path $InstallDir 'x86') -Force
        }
        Copy-Item (Join-Path $PSScriptRoot 'data\dictionary.txt') (Join-Path $InstallDir 'data') -Force
        Write-Host '      完成' -ForegroundColor Green

        Write-Host "`n[2/4] 注册 COM/TSF（regsvr32）" -ForegroundColor Cyan
        $script:Unregistering = $false
        if (Test-Path $x64) { Invoke-RegSvr32 (Join-Path $InstallDir 'x64\MyIme.dll') | Out-Null }
        if (Test-Path $x86) { Invoke-RegSvr32 (Join-Path $InstallDir 'x86\MyIme.dll') | Out-Null }

        Write-Host "`n[3/4] 检查注册结果" -ForegroundColor Cyan
        $tipKey = "HKLM:\SOFTWARE\Microsoft\CTF\TIP\$Clsid"
        if (Test-Path $tipKey) {
            Write-Host '      注册表已写入' -ForegroundColor Green
        } else {
            Write-Warning '注册表没写进去：确认是以管理员运行、且 regsvr32 没有报错'
        }

        Write-Host "`n[4/4] 怎么启用这个输入法" -ForegroundColor Cyan
        Write-Host @"
      1) 按 Win+Space 切换输入法，列表里应该能看到「$((Get-ItemProperty $tipKey -ErrorAction SilentlyContinue).'(default)')」
         （显示名来自 src/Guids.cpp 的 kTextServiceDescription）
      2) 如果列表里没有：注销再登录一次（TSF 会缓存输入法列表），
         或到「设置 → 时间和语言 → 语言和区域 → 中文(简体) → 语言选项 → 添加键盘」里找
      3) 打开记事本，切到本输入法，敲 nihao —— 应该出现虚线下划线 + 候选窗

      查看当前状态： .\install.ps1 -Action status
      看调试日志：   用 DebugView（Sysinternals）抓 OutputDebugString 输出
"@ -ForegroundColor Cyan
    }

    'uninstall' {
        Assert-Admin

        Write-Host "`n[1/3] 反注册" -ForegroundColor Cyan
        $script:Unregistering = $true
        $x64Path = Join-Path $InstallDir 'x64\MyIme.dll'
        $x86Path = Join-Path $InstallDir 'x86\MyIme.dll'
        if (Test-Path $x64Path) { Invoke-RegSvr32 $x64Path | Out-Null }
        if (Test-Path $x86Path) { Invoke-RegSvr32 $x86Path | Out-Null }
        Write-Host '      完成（如果只剩注册表残留，会被下一步一起删掉）' -ForegroundColor Green

        Write-Host "`n[2/3] 清理注册表" -ForegroundColor Cyan
        foreach ($path in @(
            "HKLM:\SOFTWARE\Microsoft\CTF\TIP\$Clsid",
            "HKLM:\SOFTWARE\Classes\CLSID\$Clsid",
            "HKLM:\SOFTWARE\WOW6432Node\Microsoft\CTF\TIP\$Clsid",
            "HKLM:\SOFTWARE\WOW6432Node\Classes\CLSID\$Clsid"
        )) {
            if (Test-Path $path) {
                Remove-Item -Recurse -Force $path -ErrorAction SilentlyContinue
                Write-Host "      删除 $path" -ForegroundColor DarkGray
            }
        }

        Write-Host "`n[3/3] 删除文件" -ForegroundColor Cyan
        if (Test-Path $InstallDir) {
            Remove-Item -Recurse -Force $InstallDir -ErrorAction SilentlyContinue
            Write-Host "      已删除 $InstallDir" -ForegroundColor Green
        }
        Write-Host "`n提示：DLL 可能仍被某些进程加载着，删不掉就注销一次再删。" -ForegroundColor Yellow
    }
}
