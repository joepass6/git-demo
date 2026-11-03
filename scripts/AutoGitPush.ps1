#requires -Version 5.1
<#
.SYNOPSIS
    海洞 Unity 项目：每日自动提交并推送到 GitHub。

.DESCRIPTION
    流程：校验仓库/remote → git add -A（受 .gitignore 约束）→ 无改动则退出 0
          → 有改动则 commit → push。
    push 失败/超时：先用 git ls-remote 探测（10 秒超时）；不可达则尝试启动 Watt Toolkit，
    之后最多等 60 秒轮询，恢复后重试 push 最多 3 次（间隔 30 秒）。
    被拒（non-fast-forward）时不 force、不 pull、不 reset，直接记录并退出非零。

.PARAMETER RepoPath
    仓库根目录，默认取脚本所在目录的上级（即 Unity 项目根）。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File "D:\桌面游戏海洞\scripts\AutoGitPush.ps1"

.NOTES
    退出码：0 成功/无改动；2 非 git 仓库；3 git 不可用；4 remote 缺失；
             5 add 失败；6 commit 失败；7 网络失败/推送失败；8 非快进被拒；9 其他异常。
#>
[CmdletBinding()]
param(
    [string]$RepoPath = '',
    [string]$RemoteName = 'origin',
    [string]$Branch = '',
    [int]$LsRemoteTimeoutSec = 10,
    [int]$PushTimeoutSec = 180,
    [int]$WattWaitSec = 60,
    [int]$PushRetryMax = 3,
    [int]$PushRetryIntervalSec = 30
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

if ([string]::IsNullOrWhiteSpace($RepoPath)) {
    if ($PSScriptRoot) { $RepoPath = Split-Path -Parent $PSScriptRoot }
    else { $RepoPath = (Get-Location).Path }
}
try { $RepoPath = (Resolve-Path -LiteralPath $RepoPath).Path }
catch { Write-Host "仓库路径不存在: $RepoPath"; exit 2 }

$script:RepoRoot = $RepoPath
$script:LogFile = ''
$script:Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

# ---------------------------------------------------------------- 日志

function Initialize-Log {
    param([string]$Root)
    $dir = Join-Path $Root 'logs\auto-push'
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $script:LogFile = Join-Path $dir ((Get-Date).ToString('yyyyMMdd') + '.log')
}

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet('INFO', 'OK', 'WARN', 'ERROR')][string]$Level = 'INFO'
    )
    $line = '[{0}][{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    Write-Host $line
    if ($script:LogFile) {
        try { Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8 -ErrorAction SilentlyContinue } catch { }
    }
}

# ---------------------------------------------------------------- 进程执行（带超时）

function Stop-ProcessTree {
    param([int]$ProcessId)
    try {
        $children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $ProcessId" -ErrorAction SilentlyContinue
        foreach ($c in $children) { Stop-ProcessTree -ProcessId ([int]$c.ProcessId) }
    } catch { }
    try { Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue } catch { }
}

function Quote-Arg {
    param([string]$Value)
    if ($Value -match '^[^ \t"]+$') { return $Value }
    return '"' + ($Value -replace '"', '\"') + '"'
}

# 说明：此处用 .NET Process 而非 Start-Process，因为部分宿主环境下
# Start-Process 返回对象的 ExitCode 为空，会把成功误判为失败。
function Invoke-Cmd {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkDir,
        [int]$TimeoutSec = 0
    )
    $argLine = @($Arguments | ForEach-Object { Quote-Arg $_ }) -join ' '

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = $argLine
    $psi.WorkingDirectory = $WorkDir
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

    $result = [pscustomobject]@{ ExitCode = -1; TimedOut = $false; Output = '' }
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    try {
        $null = $proc.Start()
        # 异步读取，避免输出缓冲区写满导致进程阻塞（死锁）
        $tOut = $proc.StandardOutput.ReadToEndAsync()
        $tErr = $proc.StandardError.ReadToEndAsync()
        $exited = $true
        if ($TimeoutSec -gt 0) {
            $exited = $proc.WaitForExit($TimeoutSec * 1000)
            if (-not $exited) {
                $result.TimedOut = $true
                Stop-ProcessTree -ProcessId ([int]$proc.Id)
            }
        }
        else {
            $proc.WaitForExit()
        }
        if ($exited) { $result.ExitCode = $proc.ExitCode }
        $out = ''
        $err = ''
        try { $out = [string]$tOut.Result } catch { }
        try { $err = [string]$tErr.Result } catch { }
        $result.Output = ($out + "`n" + $err).Trim()
    }
    catch {
        $result.Output = $_.Exception.Message
    }
    finally {
        try { $proc.Dispose() } catch { }
    }
    return $result
}

function Invoke-Git {
    param([string[]]$Arguments, [int]$TimeoutSec = 0)
    return Invoke-Cmd -FilePath 'git' -Arguments $Arguments -WorkDir $script:RepoRoot -TimeoutSec $TimeoutSec
}

# ---------------------------------------------------------------- 网络探测 / Watt Toolkit

function Test-RemoteReachable {
    $r = Invoke-Git -Arguments @('ls-remote', '--heads', $RemoteName) -TimeoutSec $LsRemoteTimeoutSec
    return (($r.ExitCode -eq 0) -and (-not $r.TimedOut))
}

function Get-WattProcesses {
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '(?i)watt|steam\+\+' })
}

function Find-WattToolkit {
    $fixed = @(
        'C:\Program Files\Watt Toolkit\Watt Toolkit.exe',
        'C:\Program Files (x86)\Watt Toolkit\Watt Toolkit.exe',
        'C:\Program Files\Steam++\Steam++.exe',
        'C:\Program Files (x86)\Steam++\Steam++.exe',
        (Join-Path $env:LOCALAPPDATA 'Programs\Watt Toolkit\Watt Toolkit.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Steam++\Steam++.exe'),
        'D:\Program Files\Watt Toolkit\Watt Toolkit.exe',
        'D:\Program Files (x86)\Watt Toolkit\Watt Toolkit.exe'
    )
    foreach ($p in $fixed) {
        if ($p -and (Test-Path -LiteralPath $p)) { return $p }
    }

    # 安装目录扫描（限定深度，避免全盘遍历）
    $roots = @(
        @{ Path = 'D:\'; Depth = 1 },
        @{ Path = 'C:\Program Files'; Depth = 2 },
        @{ Path = 'C:\Program Files (x86)'; Depth = 2 },
        @{ Path = 'D:\Program Files'; Depth = 2 },
        @{ Path = 'D:\Program Files (x86)'; Depth = 2 },
        @{ Path = 'D:\Software'; Depth = 2 },
        @{ Path = 'D:\Apps'; Depth = 2 }
    )
    foreach ($r in $roots) {
        if (-not (Test-Path -LiteralPath $r.Path)) { continue }
        try {
            $dirs = Get-ChildItem -LiteralPath $r.Path -Directory -Depth $r.Depth -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '(?i)watt|steam\+\+' }
            foreach ($d in $dirs) {
                $exe = Get-ChildItem -LiteralPath $d.FullName -Filter '*.exe' -File -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -match '(?i)watt|steam\+\+' } |
                    Select-Object -First 1
                if ($exe) { return $exe.FullName }
            }
        }
        catch { }
    }

    # 开始菜单快捷方式
    $menus = @(
        (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'),
        (Join-Path $env:AppData 'Microsoft\Windows\Start Menu\Programs')
    )
    foreach ($m in $menus) {
        if (-not (Test-Path -LiteralPath $m)) { continue }
        try {
            $lnk = Get-ChildItem -LiteralPath $m -Recurse -Filter '*.lnk' -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '(?i)watt|steam\+\+' } |
                Select-Object -First 1
            if ($lnk) { return $lnk.FullName }
        }
        catch { }
    }
    return $null
}

function Start-WattToolkit {
    $procs = Get-WattProcesses
    if ($procs.Count -gt 0) {
        Write-Log ("Watt Toolkit 已在运行: " + (($procs | ForEach-Object { $_.ProcessName }) -join ', '))
        return $true
    }
    $path = Find-WattToolkit
    if (-not $path) {
        Write-Log '未找到 Watt Toolkit 安装路径，跳过自动启动' 'WARN'
        return $false
    }
    Write-Log "尝试启动 Watt Toolkit: $path"
    try {
        Start-Process -FilePath $path -ErrorAction Stop | Out-Null
        Start-Sleep -Seconds 3
        $procs = Get-WattProcesses
        if ($procs.Count -gt 0) { Write-Log 'Watt Toolkit 已启动' 'OK' }
        else { Write-Log '启动命令已发出，但未检测到进程（可能仍需手动确认 UAC）' 'WARN' }
        return $true
    }
    catch {
        Write-Log ("启动 Watt Toolkit 失败: " + $_.Exception.Message) 'WARN'
        return $false
    }
}

function Wait-RemoteReachable {
    param([int]$TimeoutSec)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        if (Test-RemoteReachable) { return $true }
    }
    return $false
}

# ---------------------------------------------------------------- 摘要

function Write-Summary {
    param(
        [bool]$Committed = $false,
        [string]$CommitHash = '',
        [bool]$Pushed = $false,
        [string]$FailReason = ''
    )
    $script:Stopwatch.Stop()
    Write-Log '---------------- 摘要 ----------------'
    if ($Committed) { Write-Log ("提交: 是 ($CommitHash)") 'OK' } else { Write-Log '提交: 否' }
    if ($Pushed) { Write-Log '推送: 成功' 'OK' } else { Write-Log '推送: 未完成' 'WARN' }
    Write-Log ('耗时: {0:N1} 秒' -f $script:Stopwatch.Elapsed.TotalSeconds)
    if ($FailReason) { Write-Log ("失败原因: $FailReason") 'ERROR' }
    Write-Log '================ 结束 ================'
}

# ---------------------------------------------------------------- 主流程

Initialize-Log -Root $RepoPath

Write-Log '================ 自动提交开始 ================'
Write-Log "仓库: $RepoPath"
Write-Log "日志: $($script:LogFile)"

# 1. 仓库校验
if (-not (Test-Path -LiteralPath (Join-Path $RepoPath '.git'))) {
    Write-Log '指定的目录不是 git 仓库' 'ERROR'
    Write-Summary -FailReason '目录不是 git 仓库'
    exit 2
}
Write-Log '仓库校验: 通过'

# 2. git 可用性
$gitVer = Invoke-Cmd -FilePath 'git' -Arguments @('--version') -WorkDir $RepoPath -TimeoutSec 30
if ($gitVer.ExitCode -ne 0) {
    Write-Log "git 不可用: $($gitVer.Output)" 'ERROR'
    Write-Summary -FailReason 'git 不可用'
    exit 3
}
Write-Log "git 版本: $($gitVer.Output)"

# 3. remote 校验
$urlResult = Invoke-Git -Arguments @('remote', 'get-url', $RemoteName) -TimeoutSec 30
$remoteUrl = ''
if ($urlResult.ExitCode -eq 0) { $remoteUrl = $urlResult.Output.Trim() }
if (-not $remoteUrl) {
    Write-Log "未找到远程仓库 $RemoteName" 'ERROR'
    Write-Summary -FailReason "缺少 remote: $RemoteName"
    exit 4
}
Write-Log "remote($RemoteName): $remoteUrl"
if ($remoteUrl -match '(?i)github') { Write-Log 'remote 指向 GitHub: 是' 'OK' }
else { Write-Log 'remote 不指向 GitHub，继续执行' 'WARN' }

# 4. 分支
if ([string]::IsNullOrWhiteSpace($Branch)) {
    $b = Invoke-Git -Arguments @('rev-parse', '--abbrev-ref', 'HEAD') -TimeoutSec 30
    $Branch = $b.Output.Trim()
}
if ([string]::IsNullOrWhiteSpace($Branch) -or $Branch -eq 'HEAD') {
    Write-Log '当前处于分离头指针或无法识别分支' 'ERROR'
    Write-Summary -FailReason '无法识别当前分支'
    exit 9
}
Write-Log "当前分支: $Branch"

# 5. 暂存
$add = Invoke-Git -Arguments @('add', '-A') -TimeoutSec 300
if ($add.ExitCode -ne 0) {
    Write-Log "git add -A 失败: $($add.Output)" 'ERROR'
    Write-Summary -FailReason 'git add 失败'
    exit 5
}

$staged = Invoke-Git -Arguments @('diff', '--cached', '--name-only') -TimeoutSec 60
$stagedFiles = @($staged.Output -split "`r?`n" | Where-Object { $_.Trim() -ne '' })
if ($stagedFiles.Count -eq 0) {
    Write-Log '无改动，本次不提交' 'OK'
    Write-Summary -Committed $false -Pushed $false
    exit 0
}
Write-Log ("检测到改动: $($stagedFiles.Count) 个文件")
$stagedFiles | Select-Object -First 20 | ForEach-Object { Write-Log "  + $_" }
if ($stagedFiles.Count -gt 20) { Write-Log ("  ... 其余 {0} 个" -f ($stagedFiles.Count - 20)) }

# 6. 提交
$commitMsg = 'auto: {0} 自动提交' -f (Get-Date -Format 'yyyy-MM-dd HH:mm')
$msgFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($msgFile, $commitMsg, (New-Object System.Text.UTF8Encoding($false)))
try {
    $commit = Invoke-Git -Arguments @('commit', '-F', $msgFile) -TimeoutSec 300
}
finally {
    Remove-Item -LiteralPath $msgFile -Force -ErrorAction SilentlyContinue
}
if ($commit.ExitCode -ne 0) {
    Write-Log "git commit 失败: $($commit.Output)" 'ERROR'
    Write-Summary -FailReason 'commit 失败'
    exit 6
}
$hash = (Invoke-Git -Arguments @('rev-parse', '--short', 'HEAD') -TimeoutSec 30).Output.Trim()
Write-Log "提交完成: $hash  ($commitMsg)" 'OK'

# 7. 推送
function Test-Rejected {
    param([string]$Output)
    return ($Output -match '(?i)non-fast-forward|fetch first|\[rejected\]|Updates were rejected|remote contains work|behind its remote')
}

Write-Log '开始推送...'
$push = Invoke-Git -Arguments @('push', $RemoteName, $Branch) -TimeoutSec $PushTimeoutSec

$pushed = $false
$failReason = ''

if ((-not $push.TimedOut) -and ($push.ExitCode -eq 0)) {
    $pushed = $true
    Write-Log '推送成功' 'OK'
}
elseif (Test-Rejected $push.Output) {
    Write-Log "推送被拒（远端有新提交，需要人工处理）: $($push.Output)" 'ERROR'
    Write-Log '未执行 force / pull / reset / rebase，保留本地提交' 'WARN'
    Write-Summary -Committed $true -CommitHash $hash -Pushed $false -FailReason 'non-fast-forward，远端有新提交'
    exit 8
}
else {
    if ($push.TimedOut) { Write-Log "推送超时（$PushTimeoutSec 秒）" 'WARN' }
    else { Write-Log "推送失败: $($push.Output)" 'WARN' }

    # 网络问题：探测远端
    $reachable = Test-RemoteReachable
    if (-not $reachable) {
        Write-Log '远端不可达，尝试启动 Watt Toolkit' 'WARN'
        Start-WattToolkit | Out-Null
        Write-Log "等待网络恢复（最多 $WattWaitSec 秒）..."
        $reachable = Wait-RemoteReachable -TimeoutSec $WattWaitSec
        if ($reachable) { Write-Log '远端已恢复' 'OK' }
        else { Write-Log '等待超时，远端仍不可达' 'WARN' }
    }
    else {
        Write-Log '远端可达（push 失败原因可能不是网络）' 'WARN'
    }

    for ($i = 1; $i -le $PushRetryMax; $i++) {
        if (-not $reachable) { $reachable = Test-RemoteReachable }
        if (-not $reachable) { Write-Log "第 $i 次重试跳过：远端不可达" 'WARN' }
        else {
            Write-Log "第 $i 次重试推送..."
            $push = Invoke-Git -Arguments @('push', $RemoteName, $Branch) -TimeoutSec $PushTimeoutSec
            if ((-not $push.TimedOut) -and ($push.ExitCode -eq 0)) {
                $pushed = $true
                Write-Log "第 $i 次重试推送成功" 'OK'
                break
            }
            if (Test-Rejected $push.Output) {
                Write-Log "重试时被拒（non-fast-forward）: $($push.Output)" 'ERROR'
                $failReason = 'non-fast-forward，远端有新提交'
                break
            }
            Write-Log "第 $i 次重试失败: $($push.Output)" 'WARN'
            $failReason = 'push 失败（网络/认证）'
        }
        if ($i -lt $PushRetryMax) { Start-Sleep -Seconds $PushRetryIntervalSec }
    }
}

if ($pushed) {
    Write-Summary -Committed $true -CommitHash $hash -Pushed $true
    exit 0
}

if (-not $failReason) { $failReason = 'push 失败（网络不可达或认证失败）' }
Write-Log '本地提交已保留，可稍后手动 git push' 'WARN'
Write-Summary -Committed $true -CommitHash $hash -Pushed $false -FailReason $failReason
exit 7
