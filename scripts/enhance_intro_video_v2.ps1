<#
.SYNOPSIS
    Enhanced SysWatt Video Pipeline V2.
.DESCRIPTION
    - Primary recording: Desktop 2026.09.10 - 23.35.36.02.mp4 (no webcam, Ishara Lakshitha GitHub background)
    - Spliced segment: Real-time Threshold Alert & Windows Toast Notification from SysWatt Intro.mov
    - Neural voice: edge-tts en-US-AndrewMultilingualNeural (human-like natural narration)
    - Creator attribution: Ishara Lakshitha
    - Branded 176px top bar with Segoe UI typography and dynamic chapter indicators
    - Custom ambient synth soundtrack
    - NVIDIA NVENC hardware acceleration
#>

param (
    [string]$NewVideo = "C:\Users\IsharaMPC\Videos\NVIDIA\Desktop\Desktop 2026.09.10 - 23.35.36.02.mp4",
    [string]$OldVideo = "C:\Users\IsharaMPC\Videos\SysWatt Intro.mov",
    [string]$OutputVideo = "C:\Users\IsharaMPC\Videos\SysWatt_Intro_Enhanced.mp4",
    [string]$WorkDir = "C:\Users\IsharaMPC\.gemini\antigravity\brain\0d08ee75-7007-4593-9488-45fb4de25896\scratch\v2"
)

$ErrorActionPreference = "Stop"

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "     SysWatt Video Enhancement Pipeline V2" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$audioDir = "C:\Users\IsharaMPC\.gemini\antigravity\brain\0d08ee75-7007-4593-9488-45fb4de25896\scratch\neural_audio"

# ---------------------------------------------------------
# STEP 1: Mix Master Audio
# ---------------------------------------------------------
Write-Host "[1/4] Mixing Master Audio with Neural Voice..." -ForegroundColor Yellow

$musicWav = "C:\Users\IsharaMPC\.gemini\antigravity\brain\0d08ee75-7007-4593-9488-45fb4de25896\scratch\ambient_soundtrack.wav"
$masterAudio = Join-Path $WorkDir "master_audio_v2.wav"

$c0 = Join-Path $audioDir "clip_0.mp3"
$c1 = Join-Path $audioDir "clip_1.mp3"
$c2 = Join-Path $audioDir "clip_2.mp3"
$c3 = Join-Path $audioDir "clip_3.mp3"
$c4 = Join-Path $audioDir "clip_4.mp3"
$c5 = Join-Path $audioDir "clip_5.mp3"
$c6 = Join-Path $audioDir "clip_6.mp3"
$c7 = Join-Path $audioDir "clip_7.mp3"
$c8 = Join-Path $audioDir "clip_8.mp3"

$audioFilter = "[1:a]aresample=48000,aformat=channel_layouts=stereo,adelay=300|300[a0];" +
               "[2:a]aresample=48000,aformat=channel_layouts=stereo,adelay=9000|9000[a1];" +
               "[3:a]aresample=48000,aformat=channel_layouts=stereo,adelay=38000|38000[a2];" +
               "[4:a]aresample=48000,aformat=channel_layouts=stereo,adelay=52000|52000[a3];" +
               "[5:a]aresample=48000,aformat=channel_layouts=stereo,adelay=67000|67000[a4];" +
               "[6:a]aresample=48000,aformat=channel_layouts=stereo,adelay=82000|82000[a5];" +
               "[7:a]aresample=48000,aformat=channel_layouts=stereo,adelay=111000|111000[a6];" +
               "[8:a]aresample=48000,aformat=channel_layouts=stereo,adelay=125000|125000[a7];" +
               "[9:a]aresample=48000,aformat=channel_layouts=stereo,adelay=145500|145500[a8];" +
               "[a0][a1][a2][a3][a4][a5][a6][a7][a8]amix=inputs=9:normalize=0[voice];" +
               "[0:a]volume=0.18[music];" +
               "[voice][music]amix=inputs=2:normalize=0[out]"

& ffmpeg -y `
    -i $musicWav `
    -i $c0 -i $c1 -i $c2 -i $c3 -i $c4 -i $c5 -i $c6 -i $c7 -i $c8 `
    -filter_complex $audioFilter `
    -map "[out]" -c:a pcm_s16le -t 158.5 $masterAudio

# ---------------------------------------------------------
# STEP 2: Render Video Segments
# ---------------------------------------------------------
Write-Host "[2/4] Rendering Video Segments with NVIDIA NVENC..." -ForegroundColor Yellow

$introImg = Join-Path $WorkDir "intro_card.png"
$outroImg = Join-Path $WorkDir "outro_card.png"
$tb1 = Join-Path $WorkDir "topbar_ch1.png"
$tb2 = Join-Path $WorkDir "topbar_ch2.png"
$tb3 = Join-Path $WorkDir "topbar_ch3.png"
$tb4 = Join-Path $WorkDir "topbar_ch4.png"
$tb5 = Join-Path $WorkDir "topbar_ch5.png"
$tb6 = Join-Path $WorkDir "topbar_ch6.png"
$tb7 = Join-Path $WorkDir "topbar_ch7.png"

$part1Intro = Join-Path $WorkDir "p1_intro.mp4"
$part2NewA  = Join-Path $WorkDir "p2_new_a.mp4"
$part3Alert = Join-Path $WorkDir "p3_alert.mp4"
$part4NewB  = Join-Path $WorkDir "p4_new_b.mp4"
$part5Outro = Join-Path $WorkDir "p5_outro.mp4"

# 2.1 Intro Card (8.5s)
& ffmpeg -y -loop 1 -t 8.5 -i $introImg `
    -vf "format=yuv420p,fade=t=in:st=0:d=0.8" `
    -c:v h264_nvenc -preset p7 -cq 19 -b:v 0 -r 60 `
    $part1Intro

# 2.2 New Video Part A (0:00 to 0:58 = 58.0s)
# Chapter 1 (0..29), Chapter 2 (29..43), Chapter 3 (43..58)
$filterA = "[0:v]format=yuv420p[base];" +
           "[base][1:v]overlay=0:0:enable='between(t,0,29)'[v1];" +
           "[v1][2:v]overlay=0:0:enable='between(t,29,43)'[v2];" +
           "[v2][3:v]overlay=0:0:enable='gte(t,43)'[vout]"

& ffmpeg -y -ss 00:00:00 -to 00:00:58 -i $NewVideo `
    -i $tb1 -i $tb2 -i $tb3 `
    -filter_complex $filterA `
    -map "[vout]" `
    -c:v h264_nvenc -preset p7 -cq 19 -b:v 0 -r 60 `
    $part2NewA

# 2.3 Spliced Alert Segment from Old Video (0:58 to 1:13 = 15.0s)
# Shift up by 34px so SysWatt aligns with y=176, overlay Chapter 4 topbar
$filterAlert = "[0:v]crop=1920:1046:0:34,pad=1920:1080:0:0,format=yuv420p[base];" +
               "[base][1:v]overlay=0:0[vout]"

& ffmpeg -y -ss 00:00:58 -to 00:01:13 -i $OldVideo `
    -i $tb4 `
    -filter_complex $filterAlert `
    -map "[vout]" `
    -c:v h264_nvenc -preset p7 -cq 19 -b:v 0 -r 60 `
    $part3Alert

# 2.4 New Video Part B (0:58 to end = 63.47s)
# Chapter 5 (0..29), Chapter 6 (29..43), Chapter 7 (43..end)
$filterB = "[0:v]format=yuv420p[base];" +
           "[base][1:v]overlay=0:0:enable='between(t,0,29)'[v1];" +
           "[v1][2:v]overlay=0:0:enable='between(t,29,43)'[v2];" +
           "[v2][3:v]overlay=0:0:enable='gte(t,43)'[vout]"

& ffmpeg -y -ss 00:00:58 -to 00:02:01.477 -i $NewVideo `
    -i $tb5 -i $tb6 -i $tb7 `
    -filter_complex $filterB `
    -map "[vout]" `
    -c:v h264_nvenc -preset p7 -cq 19 -b:v 0 -r 60 `
    $part4NewB

# 2.5 Outro Card (13.5s)
& ffmpeg -y -loop 1 -t 13.5 -i $outroImg `
    -vf "format=yuv420p,fade=t=out:st=12.0:d=1.5" `
    -c:v h264_nvenc -preset p7 -cq 19 -b:v 0 -r 60 `
    $part5Outro

# ---------------------------------------------------------
# STEP 3: Concatenate & Multiplex Final MP4
# ---------------------------------------------------------
Write-Host "[3/4] Concatenating Video & Multiplexing Master Audio..." -ForegroundColor Yellow

$concatList = Join-Path $WorkDir "concat_v2.txt"
@"
file '$($part1Intro.Replace('\', '/'))'
file '$($part2NewA.Replace('\', '/'))'
file '$($part3Alert.Replace('\', '/'))'
file '$($part4NewB.Replace('\', '/'))'
file '$($part5Outro.Replace('\', '/'))'
"@ | Set-Content -Path $concatList -Encoding Ascii

& ffmpeg -y -f concat -safe 0 -i $concatList -i $masterAudio `
    -map 0:v -map 1:a `
    -c:v copy -c:a aac -b:a 320k -shortest `
    $OutputVideo

Write-Host "======================================================" -ForegroundColor Green
Write-Host "     SUCCESS: V2 Enhanced Video Generated!" -ForegroundColor Green
Write-Host "     Output: $OutputVideo" -ForegroundColor Green
Write-Host "======================================================" -ForegroundColor Green
