<#
用途：从主图标 WindBoard/Assets/icon.png 派生 MSIX 打包所需的视觉资产（图标），
      输出到 WindBoard/Assets/Msix/ 供 Package.appxmanifest 引用。

为什么需要：Package.appxmanifest 的 Square44x44Logo / Square150x150Logo / StoreLogo 会由
      Windows App Certification Kit（WACK）与 Partner Center 校验尺寸与 targetsize 资源，
      直接把 1080x1080 的 icon.png 当作 44x44/150x150/50x50 引用虽然能通过 makeappx（打包期不校验），
      但提交 Store 时会被拒。因此在本脚本中按官方命名约定派生出正确尺寸的资产。

脚本说明：
- 幂等：默认只补齐「缺失或尺寸不符」的文件，重复执行结果一致；需要强制重生成时加 -Force。
- 不参与默认构建：本脚本不会被任何 MSBuild Target 调用（避免给 dotnet build WindBoard.slnx 增加依赖），
  仅在图标变更或首次初始化时需要人工执行一次，并把产物提交到仓库。

重跑方式（在仓库根目录）：
  pwsh -NoLogo -NoProfile -File WindBoard/Build/GenerateMsixVisualAssets.ps1
  # 强制重生成：
  pwsh -NoLogo -NoProfile -File WindBoard/Build/GenerateMsixVisualAssets.ps1 -Force

派生清单（与 Package.appxmanifest 中的引用配套）：
- Square44x44Logo.png                        基础图 44x44
- Square44x44Logo.targetsize-<n>.png         任务栏/开始菜单按目标尺寸取图（n = 16/24/32/44/48/64/256）
- Square44x44Logo.targetsize-<n>_altform-unplated.png
                                             同尺寸的无底板（透明背景）变体，Win10/11 推荐
- Square150x150Logo.png                      150x150，另附 scale-100/200/400 变体
- StoreLogo.png                              50x50，另附 scale-100/200/400 变体
#>
param(
    [string]$SourceIcon,

    [string]$OutputDir,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($SourceIcon)) {
    $SourceIcon = Join-Path $PSScriptRoot '..\Assets\icon.png'
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $PSScriptRoot '..\Assets\Msix'
}

$sourceIconPath = (Resolve-Path -LiteralPath $SourceIcon).ProviderPath
if (!(Test-Path -LiteralPath $sourceIconPath -PathType Leaf)) {
    throw "源图标不存在：$sourceIconPath"
}

$outputDirectory = [System.IO.Directory]::CreateDirectory($OutputDir).FullName

# 需要派生的资产：Name = 输出文件名（官方 MRT 资源命名约定），Size = 正方形边长（像素）
$assets = [System.Collections.Generic.List[hashtable]]::new()

$assets.Add(@{ Name = 'Square44x44Logo.png'; Size = 44 })
foreach ($size in 16, 24, 32, 44, 48, 64, 256) {
    $assets.Add(@{ Name = "Square44x44Logo.targetsize-$size.png"; Size = $size })
    $assets.Add(@{ Name = "Square44x44Logo.targetsize-${size}_altform-unplated.png"; Size = $size })
}

$assets.Add(@{ Name = 'Square150x150Logo.png'; Size = 150 })
foreach ($scale in 100, 200, 400) {
    $assets.Add(@{ Name = "Square150x150Logo.scale-$scale.png"; Size = [int](150 * $scale / 100) })
}

$assets.Add(@{ Name = 'StoreLogo.png'; Size = 50 })
foreach ($scale in 100, 200, 400) {
    $assets.Add(@{ Name = "StoreLogo.scale-$scale.png"; Size = [int](50 * $scale / 100) })
}

$source = [System.Drawing.Image]::FromFile($sourceIconPath)
try {
    if ($source.Width -ne $source.Height) {
        # 非正方形源图会被拉伸；此处仅告警，保持脚本不因图标尺寸问题中断。
        Write-Warning "源图标非正方形（$($source.Width)x$($source.Height)），派生时会按正方形拉伸，请确认图标内容无明显变形。"
    }

    $generated = 0
    $skipped = 0

    foreach ($asset in $assets) {
        $targetPath = Join-Path $outputDirectory $asset.Name
        $size = [int]$asset.Size

        if (!$Force -and (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            $existing = [System.Drawing.Image]::FromFile($targetPath)
            try {
                if ($existing.Width -eq $size -and $existing.Height -eq $size) {
                    $skipped++
                    continue
                }
            }
            finally {
                $existing.Dispose()
            }
        }

        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.DrawImage($source, 0, 0, $size, $size)
            }
            finally {
                $graphics.Dispose()
            }

            # 先写临时文件再替换，保证重复执行时不会留下半写状态的文件。
            $tempPath = "$targetPath.tmp"
            $bitmap.Save($tempPath, [System.Drawing.Imaging.ImageFormat]::Png)
            if (Test-Path -LiteralPath $targetPath) {
                Remove-Item -LiteralPath $targetPath -Force
            }
            Move-Item -LiteralPath $tempPath -Destination $targetPath
        }
        finally {
            $bitmap.Dispose()
        }

        $generated++
    }
}
finally {
    $source.Dispose()
}

Write-Output "MSIX 视觉资产已就绪：输出目录='$outputDirectory'，新生成=$generated，已存在且尺寸正确=$skipped，源图标='$sourceIconPath'"
