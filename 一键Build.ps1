# 一键 Build + 运行 珊瑚牧歌
# 唯一打包入口：GameBuilder.Build（编辑器菜单：Build -> 打包 Windows x64）
# 统一输出：D:\BuildOut\珊瑚牧歌.exe
$ErrorActionPreference = "Continue"

$Unity      = "D:\2022.3.51f1c1\Editor\Unity.exe"
$ProjectDir = "D:\桌面游戏海洞"
$OutputPath = "D:\BuildOut\珊瑚牧歌.exe"
$LogFile    = Join-Path $env:TEMP "珊瑚牧歌Build.log"
$SavePath   = "C:\Users\64622\AppData\LocalLow\Test\珊瑚牧歌\save.json"

Write-Host "=== 杀旧进程 ===" -ForegroundColor Cyan
Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "*珊瑚*" } |
    Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "=== 清玩法进度（保留窗口状态）===" -ForegroundColor Cyan
if (Test-Path $SavePath) {
    try {
        $old = Get-Content $SavePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $ws  = $old.windowState
        if ($null -eq $ws) { throw "save.json 里没有 windowState" }
        [ordered]@{
            itemCounts        = @()
            windowState       = [ordered]@{ posX = [float]$ws.posX; posY = [float]$ws.posY; windowScale = [float]$ws.windowScale }
            seenFish          = @()
            droppedFish       = @()
            totalCoralsPlaced = 0
            totalCoralsSold   = 0
            placedObjects     = @()
        } | ConvertTo-Json -Depth 5 | Set-Content $SavePath -Encoding UTF8
        Write-Host "  已清空玩法数据，保留窗口位置=($($ws.posX), $($ws.posY)) 缩放=$($ws.windowScale)"
    } catch {
        Write-Host "  旧存档无法解析，回退为删除（窗口状态回默认）: $($_.Exception.Message)" -ForegroundColor Yellow
        Remove-Item $SavePath -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "  没有 save.json，跳过"
}

Write-Host "=== 启动 Unity Build ===" -ForegroundColor Cyan
& $Unity -batchmode -quit -accept-apiupdate `
    -projectPath $ProjectDir `
    -executeMethod GameBuilder.Build `
    -outputPath $OutputPath `
    -logFile $LogFile
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0 -and (Test-Path $OutputPath)) {
    Write-Host "✅ 成功: $OutputPath" -ForegroundColor Green
    Write-Host "=== 启动游戏 ===" -ForegroundColor Cyan
    Start-Process $OutputPath
} else {
    Write-Host "❌ 打包失败 (exit=$exitCode)，日志末尾:" -ForegroundColor Red
    Get-Content $LogFile -Tail 30 -ErrorAction SilentlyContinue
}

Write-Host "=== 按任意键关闭 ==="
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
