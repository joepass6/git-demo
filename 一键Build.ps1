# 一键 Build + 运行 珊瑚牧歌
$ErrorActionPreference = "Continue"

Write-Host "=== 杀旧进程 ===" -ForegroundColor Cyan
Get-Process | Where-Object { $_.Path -like "*珊瑚*" } | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "=== 删存档 ===" -ForegroundColor Cyan
Remove-Item "C:\Users\64622\AppData\LocalLow\Test\珊瑚牧歌\save.json" -Force -ErrorAction SilentlyContinue

Write-Host "=== 启动 Unity Build (大约 60 秒) ===" -ForegroundColor Cyan
& "D:\2022.3.51f1c1\Editor\Unity.exe" -batchmode -quit -accept-apiupdate `
    -projectPath "D:\桌面游戏海洞" `
    -executeMethod QuickBuild.BuildWindows `
    -logFile "$env:TEMP\珊瑚牧歌Build.log"

Write-Host "=== Build 完成 ===" -ForegroundColor Cyan
if (Test-Path "D:\BuildOut\珊瑚牧歌.exe") {
    $f = Get-Item "D:\BuildOut\珊瑚牧歌.exe"
    Write-Host "✅ 成功: $($f.FullName)  ($([math]::Round($f.Length/1MB,1))MB)" -ForegroundColor Green
    Write-Host "=== 启动游戏 ===" -ForegroundColor Cyan
    Start-Process "D:\BuildOut\珊瑚牧歌.exe"
} else {
    Write-Host "❌ 失败，日志:" -ForegroundColor Red
    Get-Content "$env:TEMP\珊瑚牧歌Build.log" -Tail 20 -ErrorAction SilentlyContinue
    Write-Host ""
    Write-Host "=== 尝试用 GameBuilder ===" -ForegroundColor Yellow
    & "D:\2022.3.51f1c1\Editor\Unity.exe" -batchmode -quit -accept-apiupdate `
        -projectPath "D:\桌面游戏海洞" `
        -executeMethod GameBuilder.Build `
        -outputPath "D:\BuildOut\珊瑚牧歌.exe" `
        -logFile "$env:TEMP\珊瑚牧歌Build2.log"
    
    if (Test-Path "D:\BuildOut\珊瑚牧歌.exe") {
        $f = Get-Item "D:\BuildOut\珊瑚牧歌.exe"
        Write-Host "✅ 成功: $($f.FullName)" -ForegroundColor Green
        Start-Process "D:\BuildOut\珊瑚牧歌.exe"
    } else {
        Write-Host "❌ GameBuilder 也失败了" -ForegroundColor Red
        Get-Content "$env:TEMP\珊瑚牧歌Build2.log" -Tail 20 -ErrorAction SilentlyContinue
    }
}

Write-Host "=== 按任意键关闭 ==="
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
