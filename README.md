# HA Video Reference

A reference video next to Roblox Studio that follows **Heaven's Animator**'s timeline: scrub, step or play in the
animator and your video moves to the same moment. Works with Heaven's Animator (full edition) and Heaven's Animator
Lite.

- Any video your browser can play (mp4, webm, mov ...). It plays from your computer: nothing is uploaded.
- Frame-exact while paused; while playing it stays within about a frame of Studio.
- Frame offset, mirror, sound and Picture in Picture (a small always-on-top video window).
- Windows: nothing to install (it uses the PowerShell built into Windows). It only listens on `localhost`, so nothing
  outside your computer can reach it.

## Download

Get **`HA Video Reference.zip`** from the [latest release](../../releases/latest) (or **Code › Download ZIP** on this
page) and unzip it anywhere.

## Setup (once)

1. Put **`HA Video Sync.rbxmx`** in your Studio Plugins folder (Studio › Plugins tab › **Plugins Folder**) and restart
   Studio. A toolbar tab **HA Video Reference** appears with a **Video Sync** button.

## Every time

1. Double-click **`Start Video Reference.bat`**. A small window opens (keep it open) and the player opens in your
   browser.
2. In the player: **Open Video…** (or drop a video on it).
3. In Studio: **HA Video Reference** tab › **Video Sync** › **Connect**. The first time, Studio asks to let the
   plugin reach `localhost`: allow it.
4. Animate. The video follows Heaven's Animator's frame.

## In the player

| Control | What it does |
|---|---|
| Frame offset | The video time that matches Studio's frame 0, in frames (e.g. 12 skips the first 12 frames) |
| Mirror | Flips the video left / right |
| Sound | The video plays muted unless you turn this on |
| Picture in Picture | A small always-on-top video window you can put next to Studio |

## Trouble

- **"Can't reach the app"**: the `.bat` window is closed or uses another port. Start it again; the port in the plugin
  and the window must match (default `34900`).
- **Another port**: run `"Start Video Reference.bat" 34901` from a command prompt, then type `34901` in the plugin's
  Port box.
- **Windows SmartScreen**: it is a plain script. **More info › Run anyway**, or right-click the file › Properties ›
  **Unblock**.

## How it works

`Start Video Reference.bat` runs `server.ps1`, a tiny web server on `http://localhost:34900/`. The **HA Video Sync**
plugin (source in [`plugin/VideoSync.server.lua`](plugin/VideoSync.server.lua)) posts Heaven's Animator's frame to
`/sync` (`{frame, fps, playing, speed}`, up to 30 times a second while something changes, once a second otherwise).
The player page (`player.html`) reads `/state` and moves the video: paused it seeks to the exact frame, playing it
plays at Studio's speed and corrects any drift.

Made for [Heaven's Animator](https://create.roblox.com/store/asset/84471422588789) by MrHxaven. MIT licensed.
