$csvPath = "D:\桌面游戏海洞\Assets\Resources\Localization\ui.csv"
$text = [System.IO.File]::ReadAllText($csvPath, [System.Text.Encoding]::UTF8)
# 去掉 BOM
if ($text.Length -ge 3 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }
# 再去一次（双 BOM）
if ($text.Length -ge 3 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }

$lines = $text -split "`n" | Where-Object { $_.Trim() -ne "" }

Write-Host "Total lines: $($lines.Count)"
Write-Host ""

# 表头
$header = $lines[0] -split ","
Write-Host "Header columns ($($header.Count)): $header"
Write-Host ""

# 检查语言按钮那几行
foreach ($line in $lines) {
    $cols = $line -split ","
    if ($cols.Count -ge 4 -and $cols[0] -match "^(英语|日语|韩语|德语|法语|俄语|繁体|简中|西班牙语)$") {
        Write-Host "Key=[$($cols[0])]  cols=$($cols.Count)"
        Write-Host "  简中(2): [$($cols[1])]"
        Write-Host "  English(4): [$($cols[3])]"
    }
}
