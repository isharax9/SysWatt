<#
.SYNOPSIS
    Enhances the raw SysWatt screen recording into a studio-grade product showcase video.
.DESCRIPTION
    1. Generates branded 1080p Intro & Outro title cards.
    2. Generates 7 dynamic chapter header bars (covering browser tabs & webcam box).
    3. Synthesizes a timed professional voiceover track matching each demonstrated feature.
    4. Generates an ambient electronic synth background music track.
    5. Mixes narration and background music into a 48kHz stereo master audio track.
    6. Renders the final 1080p60 video with NVIDIA NVENC hardware acceleration.
#>

param (
    [string]$InputVideo = "C:\Users\IsharaMPC\Videos\SysWatt Intro.mov",
    [string]$OutputVideo = "C:\Users\IsharaMPC\Videos\SysWatt_Intro_Enhanced.mp4",
    [string]$WorkDir = "C:\Users\IsharaMPC\.gemini\antigravity\brain\0d08ee75-7007-4593-9488-45fb4de25896\scratch\pipeline"
)

$ErrorActionPreference = "Stop"

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "     SysWatt Video Enhancement Pipeline" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan

if (-not (Test-Path $InputVideo)) {
    throw "Input video not found: $InputVideo"
}

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$audioDir = Join-Path $WorkDir "audio"
New-Item -ItemType Directory -Force -Path $audioDir | Out-Null

$logoPath = "c:\Projects\PerfMetrics\src\SysWatt.App\Assets\SysWatt-logo-source.png"
if (-not (Test-Path $logoPath)) {
    $logoPath = "c:\Projects\PerfMetrics\src\SysWatt.App\Assets\SysWatt-logo.png"
}

# ---------------------------------------------------------
# STEP 1: Generate Visual Cards & Chapter Banners
# ---------------------------------------------------------
Write-Host "[1/6] Generating Intro, Outro, and Chapter Banners..." -ForegroundColor Yellow
Add-Type -AssemblyName System.Drawing

$width = 1920
$height = 1080
$barHeight = 220

$logo = [System.Drawing.Image]::FromFile($logoPath)

# 1.1 Intro Card
$bmpIntro = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmpIntro)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

$rect = New-Object System.Drawing.Rectangle(0, 0, $width, $height)
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 15, 20, 26), [System.Drawing.Color]::FromArgb(255, 8, 10, 14), [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
$g.FillRectangle($brush, $rect)

$gridPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(25, 0, 200, 140), 1)
for ($x = 0; $x -lt $width; $x += 60) { $g.DrawLine($gridPen, $x, 0, $x, $height) }
for ($y = 0; $y -lt $height; $y += 60) { $g.DrawLine($gridPen, 0, $y, $width, $y) }

$g.DrawImage($logo, 830, 200, 260, 260)

$fontTitle = New-Object System.Drawing.Font("Segoe UI", 56, [System.Drawing.FontStyle]::Bold)
$fontSub = New-Object System.Drawing.Font("Segoe UI", 24, [System.Drawing.FontStyle]::Regular)
$fontTag = New-Object System.Drawing.Font("Segoe UI", 16, [System.Drawing.FontStyle]::Regular)
$fontBadge = New-Object System.Drawing.Font("Segoe UI", 14, [System.Drawing.FontStyle]::Bold)

$whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$grayBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 180, 195, 210))
$accentBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 220, 160))

$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center

$g.DrawString("SysWatt", $fontTitle, $whiteBrush, 960, 480, $sf)
$g.DrawString("Professional, Lightweight Windows Hardware Power & Energy Monitor", $fontSub, $accentBrush, 960, 580, $sf)
$g.DrawString("High-Precision Real-Time Telemetry · Zero-Allocation Engine · Native Windows UX", $fontTag, $grayBrush, 960, 640, $sf)

$tags = @(".NET 8 LTS", "WPF NATIVE", "LIBREHARDWAREMONITOR", "TRAFFICMONITOR-STYLE", "OPEN SOURCE")
$startX = 460
$tagY = 740
foreach ($t in $tags) {
    $size = $g.MeasureString($t, $fontBadge)
    $tw = [int]$size.Width + 30
    $th = 44
    $tagRect = New-Object System.Drawing.Rectangle($startX, $tagY, $tw, $th)
    $tagBg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(200, 22, 28, 36))
    $tagPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0, 180, 130), 1.5)
    $g.FillRectangle($tagBg, $tagRect)
    $g.DrawRectangle($tagPen, $tagRect)
    $g.DrawString($t, $fontBadge, $whiteBrush, $startX + ($tw / 2), $tagY + 10, $sf)
    $startX += $tw + 16
}
$g.Dispose()
$introImg = Join-Path $WorkDir "intro_card.png"
$bmpIntro.Save($introImg, [System.Drawing.Imaging.ImageFormat]::Png)
$bmpIntro.Dispose()

# 1.2 Outro Card
$bmpOutro = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g2 = [System.Drawing.Graphics]::FromImage($bmpOutro)
$g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g2.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

$g2.FillRectangle($brush, $rect)
for ($x = 0; $x -lt $width; $x += 60) { $g2.DrawLine($gridPen, $x, 0, $x, $height) }
for ($y = 0; $y -lt $height; $y += 60) { $g2.DrawLine($gridPen, 0, $y, $width, $y) }

$g2.DrawImage($logo, 855, 180, 210, 210)
$g2.DrawString("Get Started with SysWatt", $fontTitle, $whiteBrush, 960, 420, $sf)
$g2.DrawString("Free · Lightweight · 100% Open Source on GitHub", $fontSub, $accentBrush, 960, 520, $sf)

$boxW = 700
$boxH = 110
$boxX = [int]((1920 - $boxW) / 2)
$boxY = 600
$boxBg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(240, 20, 26, 35))
$boxPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0, 210, 160), 2)
$g2.FillRectangle($boxBg, $boxX, $boxY, $boxW, $boxH)
$g2.DrawRectangle($boxPen, $boxX, $boxY, $boxW, $boxH)

$fontBox1 = New-Object System.Drawing.Font("Segoe UI", 16, [System.Drawing.FontStyle]::Bold)
$fontBox2 = New-Object System.Drawing.Font("Segoe UI", 22, [System.Drawing.FontStyle]::Bold)
$g2.DrawString("GITHUB REPOSITORY", $fontBox1, $grayBrush, 960, $boxY + 15, $sf)
$g2.DrawString("github.com/isharax9/PerfMetrics", $fontBox2, $whiteBrush, 960, $boxY + 48, $sf)
$g2.DrawString("Created by Ishara Madusanka  ·  Star & Support on GitHub", $fontTag, $grayBrush, 960, 750, $sf)

$g2.Dispose()
$outroImg = Join-Path $WorkDir "outro_card.png"
$bmpOutro.Save($outroImg, [System.Drawing.Imaging.ImageFormat]::Png)
$bmpOutro.Dispose()

# 1.3 Chapter Topbars
$chapters = @(
    @{ Id = 1; Badge = "FEATURE 01  ·  REAL-TIME TELEMETRY"; Desc = "Live Wall Power Draw · CPU & GPU Loads · Rolling Trend Graphs" },
    @{ Id = 2; Badge = "FEATURE 02  ·  HARDWARE PARITY"; Desc = "Real-Time Sensor Correlation Verified with Native Windows Task Manager" },
    @{ Id = 3; Badge = "FEATURE 03  ·  THRESHOLD ALERTS"; Desc = "Automated Hardware Protection · In-App Banner & Windows Toast Notifications" },
    @{ Id = 4; Badge = "FEATURE 04  ·  HISTORICAL ENERGY ARCHIVE"; Desc = "TrafficMonitor-Style List View & Interactive Calendar Heatmap Matrix" },
    @{ Id = 5; Badge = "FEATURE 05  ·  TASKBAR QUICK MONITOR"; Desc = "Compact Notification Area Flyout · Rolling Trends · Desktop Pinning" },
    @{ Id = 6; Badge = "FEATURE 06  ·  POWER MODEL & SETTINGS"; Desc = "Calibrated PSU Efficiency · Idle Load Envelopes · Custom Alert Rules" },
    @{ Id = 7; Badge = "FEATURE 07  ·  HARDWARE ACCURACY"; Desc = "High-Precision Sensor Parity with Dedicated Motherboard Utilities" }
)

$fontBarTitle = New-Object System.Drawing.Font("Segoe UI", 34, [System.Drawing.FontStyle]::Bold)
$fontBarTagline = New-Object System.Drawing.Font("Segoe UI", 15, [System.Drawing.FontStyle]::Regular)
$fontBarBadge = New-Object System.Drawing.Font("Segoe UI", 13.5, [System.Drawing.FontStyle]::Bold)
$fontBarDesc = New-Object System.Drawing.Font("Segoe UI", 14, [System.Drawing.FontStyle]::Regular)

$pillX = 1020
$pillY = 48
$pillW = 860
$pillH = 124
$pillBg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 24, 30, 40))
$pillBorder = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 45, 55, 75), 1.5)

$r = 16
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc($pillX, $pillY, $r, $r, 180, 90)
$path.AddArc($pillX + $pillW - $r, $pillY, $r, $r, 270, 90)
$path.AddArc($pillX + $pillW - $r, $pillY + $pillH - $r, $r, $r, 0, 90)
$path.AddArc($pillX, $pillY + $pillH - $r, $r, $r, 90, 90)
$path.CloseFigure()

foreach ($ch in $chapters) {
    $bmp = New-Object System.Drawing.Bitmap($width, $barHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gBar = [System.Drawing.Graphics]::FromImage($bmp)
    $gBar.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gBar.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $rectBar = New-Object System.Drawing.Rectangle(0, 0, $width, $barHeight)
    $brushBar = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rectBar, [System.Drawing.Color]::FromArgb(255, 18, 22, 28), [System.Drawing.Color]::FromArgb(255, 10, 13, 17), [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $gBar.FillRectangle($brushBar, $rectBar)

    $accentPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0, 190, 140), 3)
    $gBar.DrawLine($accentPen, 0, $barHeight - 2, $width, $barHeight - 2)

    $logoSize = 145
    $logoY = [int](($barHeight - $logoSize) / 2)
    $gBar.DrawImage($logo, 40, $logoY, $logoSize, $logoSize)

    $gBar.DrawString("SysWatt", $fontBarTitle, $whiteBrush, 210, 50)
    $gBar.DrawString("Professional Windows Hardware Power & Energy Monitor", $fontBarTagline, $grayBrush, 213, 120)

    $gBar.FillPath($pillBg, $path)
    $gBar.DrawPath($pillBorder, $path)

    $gBar.DrawString($ch.Badge, $fontBarBadge, $accentBrush, $pillX + 28, $pillY + 25)
    $gBar.DrawString($ch.Desc, $fontBarDesc, $whiteBrush, $pillX + 28, $pillY + 65)

    $gBar.Dispose()
    $savePath = Join-Path $WorkDir ("topbar_ch$($ch.Id).png")
    $bmp.Save($savePath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$logo.Dispose()

# ---------------------------------------------------------
# STEP 2: Synthesize Timed Voiceover Clips
# ---------------------------------------------------------
Write-Host "[2/6] Synthesizing Voiceover Narration Track..." -ForegroundColor Yellow
Add-Type -AssemblyName System.Speech

$clips = @(
    @{ Id = 0; Text = "Welcome to SysWatt — the professional, lightweight Windows hardware power and energy monitor." },
    @{ Id = 1; Text = "Built with native WPF and a zero-allocation monitoring engine, SysWatt gives you high-precision, one-second telemetry with minimal resource overhead. The four hero cards provide instant visibility into estimated wall power draw, active CPU package watts, GPU board load, and total daily energy accumulation. Below, synchronized trend graphs track power spikes, while storage activity and multi-channel fan speeds update in real time." },
    @{ Id = 2; Text = "Hardware metrics are continuously correlated with native Windows performance counters and LibreHardwareMonitor sensor feeds. Here, running side-by-side with Windows Task Manager under full load, SysWatt tracks utilization and package power with exact precision and zero lag." },
    @{ Id = 3; Text = "SysWatt includes a built-in threshold alert system. When component temperatures or power draws exceed your configured safety limits, the app immediately triggers native Windows toast notifications and in-app warning banners to protect your hardware." },
    @{ Id = 4; Text = "Inspired by classic utilities like TrafficMonitor, the Historical Energy Statistics dialog provides complete consumption archives. You can review your daily and monthly active run-time, total kilowatt-hours, and average wattage in the List View with dynamic scaling, or explore the interactive Calendar Heatmap to visualize your energy trends across the entire month." },
    @{ Id = 5; Text = "For a minimal desktop footprint, SysWatt lives right in your Windows notification area. Clicking the system tray icon brings up the compact Quick Monitor flyout, showing live wall draw, component power breakdowns, and a three-minute rolling trend chart. You can even pin the flyout to keep it visible while working or gaming." },
    @{ Id = 6; Text = "Through the tabbed Property Sheet settings, you can calibrate PSU efficiency curves, adjust baseline system loads, configure sensor polling, and customize alert rules to fit your exact PC build." },
    @{ Id = 7; Text = "SysWatt delivers full sensor parity with dedicated manufacturer utilities while keeping CPU and RAM consumption practically at zero." },
    @{ Id = 8; Text = "SysWatt is completely free and open source. Check out the project on GitHub, download the latest release, and star the repository today!" }
)

$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.SelectVoice("Microsoft David Desktop")
$synth.Rate = 1

foreach ($c in $clips) {
    $filePath = Join-Path $audioDir ("clip_$($c.Id).wav")
    $synth.SetOutputToWaveFile($filePath)
    $synth.Speak($c.Text)
}
$synth.Dispose()

# ---------------------------------------------------------
# STEP 3: Synthesize Ambient Tech Soundtrack
# ---------------------------------------------------------
Write-Host "[3/6] Generating Ambient Synth Soundtrack..." -ForegroundColor Yellow

$musicCode = @'
using System;
using System.IO;

public class SoundtrackGen
{
    public static void Generate(string path, double durationSeconds)
    {
        int sampleRate = 48000;
        int numSamples = (int)(sampleRate * durationSeconds);
        short[] buffer = new short[numSamples * 2];

        double[][] chords = new double[][]
        {
            new double[] { 110.0, 130.81, 164.81, 220.0, 329.63, 440.0 }, // Am
            new double[] { 87.31, 110.0,  130.81, 174.61, 261.63, 349.23 }, // F
            new double[] { 65.41, 98.00,  130.81, 164.81, 261.63, 329.63 }, // C
            new double[] { 98.00, 123.47, 146.83, 196.00, 293.66, 392.00 }  // G
        };

        double chordDuration = 4.0;
        double[] arpNotes = new double[] { 440.0, 523.25, 659.25, 880.0, 659.25, 523.25 };

        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            int chordIdx = ((int)(t / chordDuration)) % chords.Length;
            double[] currentChord = chords[chordIdx];

            double masterEnv = 1.0;
            if (t < 3.0) masterEnv = t / 3.0;
            else if (t > durationSeconds - 4.0) masterEnv = Math.Max(0.0, (durationSeconds - t) / 4.0);

            double padL = 0;
            double padR = 0;
            for (int k = 0; k < currentChord.Length; k++)
            {
                double f = currentChord[k];
                double osc1 = Math.Sin(2.0 * Math.PI * f * t);
                double osc2 = Math.Sin(2.0 * Math.PI * (f * 1.003) * t + 0.5);
                double osc3 = Math.Sin(2.0 * Math.PI * (f * 0.997) * t + 1.0);
                double lfo = 0.85 + 0.15 * Math.Sin(2.0 * Math.PI * 0.25 * t + k);

                padL += (osc1 * 0.6 + osc2 * 0.4) * lfo;
                padR += (osc1 * 0.6 + osc3 * 0.4) * lfo;
            }
            padL *= 0.08;
            padR *= 0.08;

            double bassFreq = currentChord[0] * 0.5;
            double bassBeat = (t % 1.0);
            double bassEnv = Math.Exp(-3.0 * bassBeat);
            double bass = Math.Sin(2.0 * Math.PI * bassFreq * t) * bassEnv * 0.12;

            double arpStep = (t * 4.0) % arpNotes.Length;
            int arpIdx = (int)arpStep;
            double arpFreq = currentChord[arpIdx % currentChord.Length] * 2.0;
            double arpEnv = Math.Exp(-8.0 * (arpStep - arpIdx));
            double arp = Math.Sin(2.0 * Math.PI * arpFreq * t) * arpEnv * 0.04;

            double sampleL = (padL + bass + arp * 0.7) * masterEnv;
            double sampleR = (padR + bass + arp * 0.3) * masterEnv;

            sampleL = Math.Tanh(sampleL);
            sampleR = Math.Tanh(sampleR);

            buffer[i * 2]     = (short)(sampleL * 30000.0);
            buffer[i * 2 + 1] = (short)(sampleR * 30000.0);
        }

        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            bw.Write(new char[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + buffer.Length * 2);
            bw.Write(new char[] { 'W', 'A', 'V', 'E' });
            bw.Write(new char[] { 'f', 'm', 't', ' ' });
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)2);
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2 * 2);
            bw.Write((short)4);
            bw.Write((short)16);
            bw.Write(new char[] { 'd', 'a', 't', 'a' });
            bw.Write(buffer.Length * 2);

            byte[] byteBuf = new byte[buffer.Length * 2];
            Buffer.BlockCopy(buffer, 0, byteBuf, 0, byteBuf.Length);
            bw.Write(byteBuf);
        }
    }
}
'@

if (-not ([System.Management.Automation.PSTypeName]'SoundtrackGen').Type) {
    Add-Type -TypeDefinition $musicCode -Language CSharp
}
$musicWav = Join-Path $WorkDir "ambient_music.wav"
[SoundtrackGen]::Generate($musicWav, 188.0)

# ---------------------------------------------------------
# STEP 4: Mix Master Audio
# ---------------------------------------------------------
Write-Host "[4/6] Mixing Master Audio (Narration + Music Bed)..." -ForegroundColor Yellow

$masterAudio = Join-Path $WorkDir "master_audio.wav"
$c0 = Join-Path $audioDir "clip_0.wav"
$c1 = Join-Path $audioDir "clip_1.wav"
$c2 = Join-Path $audioDir "clip_2.wav"
$c3 = Join-Path $audioDir "clip_3.wav"
$c4 = Join-Path $audioDir "clip_4.wav"
$c5 = Join-Path $audioDir "clip_5.wav"
$c6 = Join-Path $audioDir "clip_6.wav"
$c7 = Join-Path $audioDir "clip_7.wav"
$c8 = Join-Path $audioDir "clip_8.wav"

$audioFilter = "[1:a]aresample=48000,aformat=channel_layouts=stereo,adelay=200|200[a0];" +
               "[2:a]aresample=48000,aformat=channel_layouts=stereo,adelay=7000|7000[a1];" +
               "[3:a]aresample=48000,aformat=channel_layouts=stereo,adelay=41500|41500[a2];" +
               "[4:a]aresample=48000,aformat=channel_layouts=stereo,adelay=62500|62500[a3];" +
               "[5:a]aresample=48000,aformat=channel_layouts=stereo,adelay=80500|80500[a4];" +
               "[6:a]aresample=48000,aformat=channel_layouts=stereo,adelay=121500|121500[a5];" +
               "[7:a]aresample=48000,aformat=channel_layouts=stereo,adelay=146500|146500[a6];" +
               "[8:a]aresample=48000,aformat=channel_layouts=stereo,adelay=170500|170500[a7];" +
               "[9:a]aresample=48000,aformat=channel_layouts=stereo,adelay=178500|178500[a8];" +
               "[a0][a1][a2][a3][a4][a5][a6][a7][a8]amix=inputs=9:normalize=0[voice];" +
               "[0:a]volume=0.20[music];" +
               "[voice][music]amix=inputs=2:normalize=0[out]"

& ffmpeg -y `
    -i $musicWav `
    -i $c0 -i $c1 -i $c2 -i $c3 -i $c4 -i $c5 -i $c6 -i $c7 -i $c8 `
    -filter_complex $audioFilter `
    -map "[out]" -c:a pcm_s16le -t 188.0 $masterAudio

# ---------------------------------------------------------
# STEP 5: Render Video Segments & Overlays
# ---------------------------------------------------------
Write-Host "[5/6] Rendering Video Segments with NVIDIA NVENC..." -ForegroundColor Yellow

$introMp4 = Join-Path $WorkDir "part1_intro.mp4"
$demoMp4  = Join-Path $WorkDir "part2_demo.mp4"
$outroMp4 = Join-Path $WorkDir "part3_outro.mp4"

# 5.1 Intro segment (6 seconds)
& ffmpeg -y -loop 1 -t 6.0 -i $introImg `
    -vf "format=yuv420p,fade=t=in:st=0:d=0.8" `
    -c:v h264_nvenc -preset p7 -cq 19 -b:v 0 -r 60 `
    $introMp4

# 5.2 Demo segment with dynamic topbar overlays (171.989 seconds)
$tb1 = Join-Path $WorkDir "topbar_ch1.png"
$tb2 = Join-Path $WorkDir "topbar_ch2.png"
$tb3 = Join-Path $WorkDir "topbar_ch3.png"
$tb4 = Join-Path $WorkDir "topbar_ch4.png"
$tb5 = Join-Path $WorkDir "topbar_ch5.png"
$tb6 = Join-Path $WorkDir "topbar_ch6.png"
$tb7 = Join-Path $WorkDir "topbar_ch7.png"

$demoFilter = "[0:v]format=yuv420p[base];" +
              "[base][1:v]overlay=0:0:enable='between(t,0,35)'[v1];" +
              "[v1][2:v]overlay=0:0:enable='between(t,35,56)'[v2];" +
              "[v2][3:v]overlay=0:0:enable='between(t,56,74)'[v3];" +
              "[v3][4:v]overlay=0:0:enable='between(t,74,115)'[v4];" +
              "[v4][5:v]overlay=0:0:enable='between(t,115,140)'[v5];" +
              "[v5][6:v]overlay=0:0:enable='between(t,140,164)'[v6];" +
              "[v6][7:v]overlay=0:0:enable='gte(t,164)'[vout]"

& ffmpeg -y -i $InputVideo `
    -i $tb1 -i $tb2 -i $tb3 -i $tb4 -i $tb5 -i $tb6 -i $tb7 `
    -filter_complex $demoFilter `
    -map "[vout]" `
    -c:v h264_nvenc -preset p7 -cq 19 -b:v 0 -r 60 `
    $demoMp4

# 5.3 Outro segment (10 seconds)
& ffmpeg -y -loop 1 -t 10.0 -i $outroImg `
    -vf "format=yuv420p,fade=t=out:st=8.5:d=1.5" `
    -c:v h264_nvenc -preset p7 -cq 19 -b:v 0 -r 60 `
    $outroMp4

# ---------------------------------------------------------
# STEP 6: Concat & Multiplex Final MP4
# ---------------------------------------------------------
Write-Host "[6/6] Multiplexing Final Enhanced Video..." -ForegroundColor Yellow

$concatList = Join-Path $WorkDir "concat.txt"
@"
file '$($introMp4.Replace('\', '/'))'
file '$($demoMp4.Replace('\', '/'))'
file '$($outroMp4.Replace('\', '/'))'
"@ | Set-Content -Path $concatList -Encoding Ascii

& ffmpeg -y -f concat -safe 0 -i $concatList -i $masterAudio `
    -map 0:v -map 1:a `
    -c:v copy -c:a aac -b:a 320k -shortest `
    $OutputVideo

Write-Host "======================================================" -ForegroundColor Green
Write-Host "     SUCCESS: Enhanced video generated!" -ForegroundColor Green
Write-Host "     Saved to: $OutputVideo" -ForegroundColor Green
Write-Host "======================================================" -ForegroundColor Green
