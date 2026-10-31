#requires -Version 5.1
<#
.SYNOPSIS
    Generates the three synthetic demo media files used by the demo corpus.

.DESCRIPTION
    Produces, with no third-party asset and no network access:
      * ahlena-demo.jpg           — a JPEG carrying a visible "AHLENA DEMO" mark.
                                   The single public image reused by every
                                   public image field.
      * ahlena-demo-document.pdf  — a one-page PDF whose every line is demo
                                   labelled ("DEMO — not legal text"). Reused by
                                   every document field and chat attachment.
      * ahlena-demo.mp4           — a short H.264 MP4 rendered from that still.
                                   `video/mp4` is accepted only by the private
                                   storage writer, so this file is referenced
                                   only by private/video-capable fields.

    Windows-only developer tool. The still is drawn with System.Drawing and the
    document is written as a hand-assembled PDF (no PDF library). The MP4 is
    rendered through the first encoder that works, in this order:

      1. Windows' built-in media pipeline (WinRT `MediaClip.CreateFromImageFile`
         + `MediaComposition`). The brief's preferred path: it needs no external
         tool. PowerShell 5.1 cannot call the projected WinRT vector
         (`MediaComposition.Clips.Add`), so a ~30-line C# helper is compiled with
         the in-box compiler against the installed Windows SDK metadata.
      2. A discovered `ffmpeg.exe` (PATH first, then a bounded search of the
         installed-program roots), used only when it reports an H.264 encoder.
         The brief recorded "ffmpeg absent", which is not true of every machine,
         so this is a validated fallback rather than an assumption; the tool
         still refuses to emit a placeholder.

    Whichever encoder produced it, the MP4 is re-opened and its ISO-BMFF
    brand/boxes (ftyp + moov + avc1) are asserted, and it is re-opened through
    the decoder, so an invalid container can never be produced.

    The still cannot carry burned-in text inside the video without a subtitle
    burn-in pass; the demo labelling is carried by the file name, the seeded
    title/description fields and the DEMO poster image. This is the documented
    D1 limitation.

.PARAMETER OutputDirectory
    Destination directory. Defaults to the approved private temp directory.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File docs\internal\tools\New-DemoMedia.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = 'C:\Users\mosad\AppData\Local\Temp\opencode\demo-media',
    [int] $VideoSeconds = 4,
    [int] $Width = 1280,
    [int] $Height = 720
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing

function Write-DemoStill {
    param([string] $Path)

    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

            $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 15, 84, 100))
            $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 232, 176, 75))
            $graphics.FillRectangle($background, 0, 0, $Width, $Height)
            $graphics.FillRectangle($accent, 0, [int]($Height * 0.72), $Width, [int]($Height * 0.05))

            $titleFont = New-Object System.Drawing.Font('Segoe UI', 96, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
            $subtitleFont = New-Object System.Drawing.Font('Segoe UI', 34, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
            $smallFont = New-Object System.Drawing.Font('Segoe UI', 24, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
            $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
            $muted = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 214, 232, 236))
            $centre = New-Object System.Drawing.StringFormat
            $centre.Alignment = [System.Drawing.StringAlignment]::Center

            $graphics.DrawString('AHLENA DEMO', $titleFont, $white, [float]($Width / 2), [float]($Height * 0.28), $centre)
            $graphics.DrawString('Synthetic media - not a real photograph', $subtitleFont, $muted, [float]($Width / 2), [float]($Height * 0.49), $centre)
            $graphics.DrawString('generated locally and reused by every public image field', $smallFont, $muted, [float]($Width / 2), [float]($Height * 0.60), $centre)

            $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Jpeg)
        }
        finally {
            $graphics.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

function New-DemoDocument {
    param([string] $Path)

    # A one-page PDF assembled by hand: no PDF library is installed and the
    # brief forbids a stock asset, so the objects, the stream and the xref table
    # are written directly. All text is ASCII Latin-1 so the byte length of each
    # line equals its character length, which keeps the xref offsets exact.
    $lines = @(
        'AHLENA DEMO',
        'Synthetic demo document - not a real legal or clinical document.',
        '',
        'Generated locally by docs/internal/tools/New-DemoMedia.ps1 and reused',
        'by every demo-corpus document field (facility documents, caregiver',
        'papers, chat attachments). It contains no real legal, medical or',
        'personal data and no signature.',
        '',
        'DEMO - not legal text',
        'DEMO - not legal text',
        'DEMO - not legal text'
    )
    $escape = {
        param([string] $value)
        $value.Replace('\', '\\').Replace('(', '\(').Replace(')', '\)')
    }
    $stream = New-Object System.Text.StringBuilder
    [void]$stream.Append("BT`n/F1 30 Tf`n72 750 Td`n(& $escape $lines[0]) Tj`nET`n")
    [void]$stream.Append("0.2 0.2 0.2 RG`n1 w`n72 735 m 523 735 l S`n")
    $y = 700
    for ($i = 1; $i -lt $lines.Count; $i++) {
        [void]$stream.Append("BT`n/F1 13 Tf`n72 $y Td`n(& $escape $lines[$i]) Tj`nET`n")
        $y -= 22
    }
    $streamText = $stream.ToString()

    $objects = @(
        '<< /Type /Catalog /Pages 2 0 R >>'
        '<< /Type /Pages /Kids [3 0 R] /Count 1 >>'
        '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>'
        '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>'
        "<< /Length $([System.Text.Encoding]::ASCII.GetByteCount($streamText)) >>`nstream`n$streamText`nendstream"
        '<< /Title (AHLENA DEMO document) /Subject (Synthetic demo data) /Producer (New-DemoMedia.ps1) /Creator (Ahlena demo corpus) >>'
    )

    $encoding = [System.Text.Encoding]::ASCII
    $buffer = New-Object System.IO.MemoryStream
    try {
        $buffer.Write($encoding.GetBytes("%PDF-1.4`n"), 0, 9)
        $offsets = New-Object System.Collections.Generic.List[int]
        for ($index = 0; $index -lt $objects.Count; $index++) {
            $offsets.Add([int]$buffer.Position)
            $body = "$($index + 1) 0 obj`n$($objects[$index])`nendobj`n"
            $bytes = $encoding.GetBytes($body)
            $buffer.Write($bytes, 0, $bytes.Length)
        }

        $startXref = [int]$buffer.Position
        $xref = New-Object System.Text.StringBuilder
        [void]$xref.Append("xref`n0 $($objects.Count + 1)`n")
        [void]$xref.Append("0000000000 65535 f `n")
        foreach ($offset in $offsets) {
            [void]$xref.Append(('{0:D10} 00000 n ' -f $offset) + "`n")
        }
        [void]$xref.Append("trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R /Info 6 0 R >>`n")
        [void]$xref.Append("startxref`n$startXref`n%%EOF`n")
        $tail = $encoding.GetBytes($xref.ToString())
        $buffer.Write($tail, 0, $tail.Length)

        [System.IO.File]::WriteAllBytes($Path, $buffer.ToArray())
    }
    finally {
        $buffer.Dispose()
    }

    # Prove the file is a real, self-consistent PDF: the header, the trailer and
    # every xref offset resolving to its own "<n> 0 obj" are checked, so a
    # truncated or offset-broken document fails here instead of at the reader.
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $text = $encoding.GetString($bytes)
    if (-not $text.StartsWith('%PDF-1.4') -or -not $text.Contains('%%EOF')) {
        throw 'The generated document is not a PDF container (header/trailer missing).'
    }
    for ($index = 0; $index -lt $offsets.Count; $index++) {
        $head = $encoding.GetString($bytes, $offsets[$index], 10)
        if (-not $head.StartsWith("$($index + 1) 0 obj")) {
            throw "The generated PDF xref offset $($offsets[$index]) does not resolve to object $($index + 1) (found '$head')."
        }
    }
}

function Compile-DemoVideoHelper {
    param([string] $BuildDirectory)

    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $compiler)) { throw "The in-box C# compiler was not found at $compiler." }

    $sdkRoots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits'),
        (Join-Path $env:ProgramFiles 'Windows Kits')
    ) | Where-Object { $_ -and (Test-Path $_) }

    $winmd = $sdkRoots |
        ForEach-Object { Get-ChildItem $_ -Recurse -Filter Windows.winmd -ErrorAction SilentlyContinue } |
        Where-Object { $_.FullName -notmatch '\\Facades?\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $winmd) { throw 'Windows.winmd was not found; the Windows SDK media metadata is required.' }

    $runtimeWinRt = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll'

    # The System.Runtime facade is what the in-box compiler needs for the WinRT
    # projections. It lives under the 32-bit reference-assembly root on a normal
    # machine, so both roots are searched (only testing ProgramFiles is the bug
    # that used to make this helper uncompilable).
    $facadeRoots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework'),
        (Join-Path $env:ProgramFiles 'Reference Assemblies\Microsoft\Framework\.NETFramework')
    ) | Where-Object { $_ -and (Test-Path $_) }

    $runtimeFacade = $facadeRoots |
        ForEach-Object { Get-ChildItem $_ -Recurse -Filter System.Runtime.dll -ErrorAction SilentlyContinue } |
        Where-Object { $_.FullName -match '\\Facades\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $runtimeFacade) {
        throw 'The System.Runtime reference facade was not found; the Windows SDK .NET Framework facades are required.'
    }

    $source = Join-Path $BuildDirectory 'DemoVideoRenderer.cs'
    $assembly = Join-Path $BuildDirectory 'DemoVideoRenderer.dll'
    @'
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

// Renders a short H.264 MP4 from a still through Windows' built-in media
// pipeline and proves the result decodes again. No third-party asset and no
// network access.
public static class DemoVideoRenderer
{
    public static string Render(string imagePath, string videoPath, int seconds)
    {
        return RenderAsync(imagePath, videoPath, seconds).GetAwaiter().GetResult();
    }

    private static async Task<string> RenderAsync(string imagePath, string videoPath, int seconds)
    {
        StorageFile imageFile = await StorageFile.GetFileFromPathAsync(imagePath);
        MediaClip clip = await MediaClip.CreateFromImageFileAsync(imageFile, TimeSpan.FromSeconds(seconds));
        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(videoPath));
        StorageFile output = await folder.CreateFileAsync(Path.GetFileName(videoPath), CreationCollisionOption.ReplaceExisting);
        MediaEncodingProfile profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        TranscodeFailureReason reason = await composition.RenderToFileAsync(output, MediaTrimmingPreference.Fast, profile);
        if (reason != TranscodeFailureReason.None)
        {
            throw new InvalidOperationException("Transcode failed: " + reason);
        }

        // Re-open through the same pipeline: an invalid container or a missing
        // video track fails here instead of shipping a placeholder.
        StorageFile rendered = await StorageFile.GetFileFromPathAsync(videoPath);
        MediaClip decoded = await MediaClip.CreateFromFileAsync(rendered);
        if (decoded == null || decoded.OriginalDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("The rendered MP4 could not be re-opened as a decodable clip.");
        }

        return decoded.OriginalDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture);
    }
}
'@ | Set-Content -LiteralPath $source -Encoding ASCII

    $compileOutput = & $compiler /nologo /target:library /out:$assembly /r:$runtimeWinRt "/r:$($runtimeFacade.FullName)" "/r:$($winmd.FullName)" $source 2>&1
    $compileExit = $LASTEXITCODE
    if ($compileExit -ne 0 -or -not (Test-Path $assembly)) {
        $detail = ($compileOutput | ForEach-Object { "$_" }) -join ' | '
        throw "The demo video helper could not be compiled against the Windows SDK metadata (exit $compileExit): $detail"
    }

    return $assembly
}

function Find-DemoFfmpeg {
    $onPath = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $roots = @(
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)},
        (Join-Path $env:LOCALAPPDATA 'Programs')
    ) | Where-Object { $_ -and (Test-Path $_) }

    $found = $roots |
        ForEach-Object { Get-ChildItem $_ -Filter ffmpeg.exe -Recurse -Depth 4 -ErrorAction SilentlyContinue } |
        Select-Object -First 1
    if ($found) { return $found.FullName }

    return $null
}

function Get-DemoSha256 {
    param([string] $Path)

    # .NET rather than Get-FileHash: this script also runs from the in-box
    # Windows PowerShell, whose module autoloading is not guaranteed.
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            return [System.BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '')
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $algorithm.Dispose()
    }
}

function Assert-DemoMp4 {
    param([string] $Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $brand = [System.Text.Encoding]::ASCII.GetString($bytes, 4, 4)
    $all = [System.Text.Encoding]::ASCII.GetString($bytes)
    if ($brand -ne 'ftyp' -or -not $all.Contains('moov') -or -not $all.Contains('avc1')) {
        throw "The rendered file is not an H.264 MP4 (brand='$brand', moov=$($all.Contains('moov')), avc1=$($all.Contains('avc1')))."
    }

    if ($bytes.LongLength -ge 5MB) {
        throw "The rendered MP4 is $($bytes.LongLength) bytes; the private storage writer caps a single file at 5 MB."
    }

    return $brand
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$buildDirectory = Join-Path $OutputDirectory 'build'
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null

$imagePath = Join-Path $OutputDirectory 'ahlena-demo.jpg'
$documentPath = Join-Path $OutputDirectory 'ahlena-demo-document.pdf'
$videoPath = Join-Path $OutputDirectory 'ahlena-demo.mp4'

Write-DemoStill -Path $imagePath
New-DemoDocument -Path $documentPath

# Preferred: Windows' built-in media pipeline. Fallback: a discovered ffmpeg
# that can actually encode H.264. The first that succeeds wins; both results go
# through the identical container assertion below.
$encoder = $null
$duration = $null
$winRtError = $null

try {
    $helper = Compile-DemoVideoHelper -BuildDirectory $buildDirectory
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    [System.Reflection.Assembly]::LoadFrom($helper) | Out-Null
    $duration = [DemoVideoRenderer]::Render($imagePath, $videoPath, $VideoSeconds)
    $encoder = 'Windows.Media.Editing (WinRT MediaComposition)'
}
catch {
    $winRtError = $_.Exception.Message
}

if ($null -eq $encoder) {
    $ffmpeg = Find-DemoFfmpeg
    if (-not $ffmpeg) {
        throw "No demo MP4 encoder is available. The Windows media pipeline failed ($winRtError) and no ffmpeg.exe was found on PATH or under the installed-program roots."
    }

    $encoders = (& $ffmpeg -hide_banner -encoders 2>&1 | Out-String)
    # libx264 is the encoder the brief expected; h264_mf is the same
    # Media Foundation H.264 encoder the WinRT pipeline uses, and is what the
    # bundled ffmpeg builds on this machine actually ship.
    $h264Encoder = $null
    if ($encoders -match 'libx264') { $h264Encoder = 'libx264' }
    elseif ($encoders -match 'h264_mf') { $h264Encoder = 'h264_mf' }
    if (-not $h264Encoder) {
        throw "The discovered ffmpeg at '$ffmpeg' reports no H.264 encoder (libx264/h264_mf); refusing to emit a non-H.264 placeholder. Windows pipeline error: $winRtError"
    }

    $arguments = @(
        '-y', '-hide_banner', '-loglevel', 'error',
        '-loop', '1', '-framerate', '25', '-i', $imagePath,
        '-t', "$VideoSeconds",
        '-vf', "scale=${Width}:${Height},format=yuv420p",
        '-c:v', $h264Encoder
    )
    if ($h264Encoder -eq 'libx264') {
        $arguments += @('-preset', 'veryfast')
    }
    $arguments += @('-pix_fmt', 'yuv420p', '-movflags', '+faststart', '-an', $videoPath)

    & $ffmpeg @arguments
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $videoPath)) {
        throw "The discovered ffmpeg at '$ffmpeg' failed to render the demo MP4 (exit $LASTEXITCODE). Windows pipeline error: $winRtError"
    }

    $duration = "$VideoSeconds"
    $encoder = "ffmpeg ($ffmpeg, $h264Encoder)"
}

$brand = Assert-DemoMp4 -Path $videoPath

$image = Get-Item -LiteralPath $imagePath
$document = Get-Item -LiteralPath $documentPath
$video = Get-Item -LiteralPath $videoPath

[pscustomobject]@{
    ImagePath            = $image.FullName
    ImageBytes           = $image.Length
    ImageSha256          = Get-DemoSha256 -Path $imagePath
    DocumentPath         = $document.FullName
    DocumentBytes        = $document.Length
    DocumentSha256       = Get-DemoSha256 -Path $documentPath
    VideoPath            = $video.FullName
    VideoBytes           = $video.Length
    VideoSha256          = Get-DemoSha256 -Path $videoPath
    VideoDurationSeconds = $duration
    VideoBrand           = $brand
    VideoCodec           = 'avc1'
    VideoEncoder         = $encoder
    WinRtFallbackReason  = if ($winRtError) { $winRtError } else { '' }
} | Format-List
