param(
    [string]$ProjectDirectory = (Join-Path $PSScriptRoot '..\src\AgentNotifications.App'),
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\icon\icon.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assetDirectory = Join-Path $ProjectDirectory 'Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

function New-AgentNotificationIcon {
    param(
        [Parameter(Mandatory)] [System.Drawing.Image]$Source,
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [int]$Height
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($Source, [System.Drawing.Rectangle]::new(0, 0, $Width, $Height))
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$resolvedSource = (Resolve-Path $SourcePath).Path
$source = [System.Drawing.Image]::FromFile($resolvedSource)
try {
    New-AgentNotificationIcon -Source $source -Path (Join-Path $assetDirectory 'AppIcon.png') -Width 256 -Height 256
    New-AgentNotificationIcon -Source $source -Path (Join-Path $assetDirectory 'StoreLogo.png') -Width 50 -Height 50
    New-AgentNotificationIcon -Source $source -Path (Join-Path $assetDirectory 'Square44x44Logo.png') -Width 44 -Height 44
    New-AgentNotificationIcon -Source $source -Path (Join-Path $assetDirectory 'Square150x150Logo.png') -Width 150 -Height 150
}
finally {
    $source.Dispose()
}
