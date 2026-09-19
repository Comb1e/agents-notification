param(
    [string]$ProjectDirectory = (Join-Path $PSScriptRoot '..\src\AgentNotifications.App')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assetDirectory = Join-Path $ProjectDirectory 'Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

function New-AgentNotificationIcon {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [int]$Height
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::FromArgb(22, 101, 52))
        $size = [Math]::Min($Width, $Height)
        $penWidth = [Math]::Max(2, [int]($size * 0.065))
        $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, $penWidth)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        try {
            $centerX = $Width / 2.0
            $top = $Height * 0.25
            $bottom = $Height * 0.68
            $left = $centerX - ($size * 0.22)
            $right = $centerX + ($size * 0.22)
            $graphics.DrawArc($pen, $left, $top, $right - $left, ($bottom - $top) * 1.25, 190, 160)
            $graphics.DrawLine($pen, $left - ($size * 0.03), $bottom, $right + ($size * 0.03), $bottom)
            $graphics.DrawLine($pen, $centerX, $top - ($size * 0.08), $centerX, $top)
            $graphics.DrawArc($pen, $centerX - ($size * 0.06), $bottom - ($size * 0.02), $size * 0.12, $size * 0.12, 0, 180)
        }
        finally {
            $pen.Dispose()
        }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

New-AgentNotificationIcon -Path (Join-Path $assetDirectory 'StoreLogo.png') -Width 50 -Height 50
New-AgentNotificationIcon -Path (Join-Path $assetDirectory 'Square44x44Logo.png') -Width 44 -Height 44
New-AgentNotificationIcon -Path (Join-Path $assetDirectory 'Square150x150Logo.png') -Width 150 -Height 150
