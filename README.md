<p align="center"><img src="logo.png" width="180" alt="HA Video Reference logo"></p>

# HA Video Reference

A reference video next to Roblox Studio that follows **Heaven's Animator**'s timeline: scrub, step or play in the
animator and your video moves to the exact same frame. Works with Heaven's Animator (full edition) and Heaven's
Animator Lite.

- A small Windows app, not a browser page: it uses Windows' own video decoder, so every frame is exact.
- Your video plays from your computer. Nothing is uploaded.
- The app and the Studio panel wear Heaven's Animator's theme.
- Blender-style controls:
  - Start (Earlier / Later, Start Here)
  - Trim and Speed
  - Loop
  - Mute and Volume
  - Mirror
  - Always on top
  - Pan and zoom
- Windows 10 / 11, nothing to install. It only listens on `localhost`, so nothing outside your computer can reach it.

## Download

[![Download HA Video Reference](https://img.shields.io/badge/Download-HA%20Video%20Reference-ffb02e?style=for-the-badge)](https://github.com/MrHxaven/ha-video-reference/raw/main/HA-Video-Reference.zip)

The button always downloads the newest version ([`HA-Video-Reference.zip`](HA-Video-Reference.zip)). Unzip it
anywhere.

## Setup (once)

1. Put **`HA Video Sync.rbxmx`** in your Studio Plugins folder (Studio › Plugins tab › **Plugins Folder**).
2. Restart Studio. A toolbar tab **HA Video Reference** appears with a **Video Sync** button.

## Every time

1. Open **`HA Video Reference.exe`** and open a video in it. You can use **Open Video…**, drop a video on the window,
   or drop it on the .exe.
2. In Studio, go to the **HA Video Reference** tab › **Video Sync** and click **Connect**. The port must match (34900
   by default).
   - The first time, Studio asks to let the plugin reach `localhost`. Allow it.
3. Animate. The video follows Heaven's Animator's frame.

## In the app

| Control | What it does |
|---|---|
| Start | The Studio frame where the video starts. **Earlier / Later** (−10 −1 +1 +10) nudge it; **Start Here** uses Studio's current frame |
| Trim | Video frames skipped at the video's start |
| Speed | How fast the video plays against Studio (1 = real time) |
| Loop | Plays the video again from Start when it ends (otherwise it holds its last frame) |
| Mute / Volume | The video's sound |
| Mirror | Flips the video left / right |
| Always on top | Keeps the window above Studio |
| Middle mouse / wheel / Home | Pan / zoom / fit |

Settings are remembered. Phone videos show the right way up.

## Trouble

- **"App not found"**: the app is closed or uses another port. Open it again; the port in the plugin and the app must
  match (default `34900`).
- **Another port**: run `"HA Video Reference.exe" 34901` from a command prompt, then type `34901` in the plugin's Port
  box.
- **"Can't play that video"**: Windows has no decoder for it. An mp4 (H.264) always works.
- **Windows SmartScreen**: the app isn't signed. Click **More info › Run anyway**, or right-click the .exe ›
  Properties › **Unblock**. The full source is in [`app/VideoReference.cs`](app/VideoReference.cs) if you'd rather
  check it.

## How it works

**`HA Video Reference.exe`** ([`app/VideoReference.cs`](app/VideoReference.cs)) is one C# file. Windows builds it with
its own `csc.exe`, so it needs no install. It does three jobs:

- runs a tiny web server on `http://localhost:34900/`;
- decodes the video with Media Foundation;
- shows it in a WPF window.

The **HA Video Sync** plugin ([`plugin/VideoSync.server.lua`](plugin/VideoSync.server.lua)) posts Heaven's Animator's
frame to `/sync` as `{frame, fps, playing, speed, theme}`:

- up to 30 times a second while something changes;
- once a second otherwise.

The app maps Studio's frame through Start, Trim, Speed and Loop to a video frame, then decodes exactly that frame.

Made for [Heaven's Animator](https://create.roblox.com/store/asset/84471422588789) by MrHxaven. MIT licensed.
