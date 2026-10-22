param([string]$Game)
$ErrorActionPreference = 'Stop'
try {
    if (-not $Game) {
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = '选择包含 ReincarnationSquad.exe 的游戏目录'
        if ($dialog.ShowDialog() -ne 'OK') { exit 1 }
        $Game = $dialog.SelectedPath
    }
    $Game = (Resolve-Path -LiteralPath $Game).Path
    if (-not (Test-Path -LiteralPath (Join-Path $Game 'ReincarnationSquad.exe') -PathType Leaf)) { throw '该目录中没有 ReincarnationSquad.exe。' }
    function Assert-Closed {
        if (Get-Process -Name ReincarnationSquad -ErrorAction SilentlyContinue) { throw '请正常退出游戏后重新安装。' }
    }
    Assert-Closed
    $bundle = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'BUNDLE.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $ids = @('yixian.sect', 'appearance.muyifeng')
    foreach ($row in $bundle.inventory) {
        if ($row.path -notmatch '^Mods/(yixian\.sect|appearance\.muyifeng)/' -or $row.path -match '\.\.|:|\\') { throw '安装清单路径无效。' }
        $file = Join-Path $PSScriptRoot $row.path
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $row.sha256) { throw "安装包损坏，请重新解压：$($row.path)" }
    }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-ffff'
    $backup = Join-Path $Game "ModBackups/yixian-$stamp"
    $stage = Join-Path $Game ".yixian-install-$stamp"
    New-Item -ItemType Directory -Path $backup, $stage -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Game 'Mods') -Force | Out-Null
    $switched = New-Object System.Collections.ArrayList
    try {
        foreach ($id in $ids) {
            $target = Join-Path $Game "Mods/$id"
            if ((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '目标Mod目录不能是链接。' }
            if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination (Join-Path $backup $id) -Recurse }
            foreach ($row in $bundle.inventory | Where-Object { $_.path.StartsWith("Mods/$id/") }) {
                $relative = $row.path.Substring(5)
                $destination = Join-Path $stage $relative
                New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
                Copy-Item -LiteralPath (Join-Path $PSScriptRoot $row.path) -Destination $destination
            }
            $manifest = Get-Content -LiteralPath (Join-Path $stage "$id/mod.json") -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($manifest.id -ne $id -or -not (Test-Path -LiteralPath (Join-Path $stage "$id/$($manifest.code.assembly)"))) { throw "组件不完整：$id" }
        }
        Assert-Closed
        foreach ($id in $ids) {
            $target = Join-Path $Game "Mods/$id"
            $old = Join-Path $stage "$id.previous"
            if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $old }
            $null = $switched.Add(@{target=$target;old=$old})
            Move-Item -LiteralPath (Join-Path $stage $id) -Destination $target
        }
        foreach ($row in $bundle.inventory) {
            if ((Get-FileHash -LiteralPath (Join-Path $Game $row.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $row.sha256) { throw "安装后校验失败：$($row.path)" }
        }
        @{installed=$true;game=$Game;backup=$backup;components=$bundle.components} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backup 'receipt.json') -Encoding UTF8
    } catch {
        for ($i=$switched.Count-1; $i -ge 0; $i--) {
            $item = $switched[$i]
            if (Test-Path -LiteralPath $item.target) { Remove-Item -LiteralPath $item.target -Recurse -Force }
            if (Test-Path -LiteralPath $item.old) { Move-Item -LiteralPath $item.old -Destination $item.target }
        }
        throw
    } finally {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    }
    Write-Host "安装完成。旧包备份：$backup"
    Write-Host '启动游戏，在Mod管理中启用所需包，并按提示确认代码许可。'
} catch {
    Write-Error $_
    exit 1
}
