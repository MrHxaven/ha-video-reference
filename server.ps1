# HA Video Reference: a tiny local server (Windows PowerShell, nothing to install).
# The "HA Video Sync" Studio plugin posts Heaven's Animator's frame here; the player page (opened in your browser)
# reads it and moves your reference video to the same moment.
#   GET  /         the player page (player.html)
#   GET  /state    {frame, fps, playing, speed, age, seq}   (age = seconds since Studio last synced; -1 = never)
#   POST /sync     {frame, fps, playing, speed}             (from the plugin)
#   GET  /ping     {ok, app, version}
# Only listens on localhost: nothing outside this computer can reach it.
param([int]$Port = 34900, [switch]$NoBrowser)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$playerPath = Join-Path $here 'player.html'
$version = 1

$state = @{ frame = 0.0; fps = 60.0; playing = $false; speed = 1.0; seq = 0 }
$lastSync = $null

function Send-Bytes($res, [byte[]]$bytes, [string]$type, [int]$code = 200) {
	$res.StatusCode = $code
	$res.ContentType = $type
	$res.Headers['Cache-Control'] = 'no-store'
	$res.ContentLength64 = $bytes.Length
	$res.OutputStream.Write($bytes, 0, $bytes.Length)
}

function Send-Json($res, $obj, [int]$code = 200) {
	$json = $obj | ConvertTo-Json -Compress
	Send-Bytes $res ([Text.Encoding]::UTF8.GetBytes($json)) 'application/json; charset=utf-8' $code
}

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
try {
	$listener.Start()
} catch {
	Write-Host ""
	Write-Host "  Could not start on port $Port (is it already running, or used by another program?)" -ForegroundColor Red
	Write-Host "  $($_.Exception.Message)"
	Write-Host ""
	Read-Host "Press Enter to close"
	exit 1
}

Write-Host ""
Write-Host "  HA Video Reference is running at http://localhost:$Port/" -ForegroundColor Cyan
Write-Host "  Keep this window open while you animate. Close it to stop."
Write-Host "  In Studio: HA Video Reference tab > Video Sync > port $Port > Connect."
Write-Host ""
if (-not $NoBrowser) { Start-Process "http://localhost:$Port/" }

while ($listener.IsListening) {
	$ctx = $listener.GetContext()
	$req = $ctx.Request
	$res = $ctx.Response
	try {
		$path = $req.Url.AbsolutePath
		if ($req.HttpMethod -eq 'POST' -and $path -eq '/sync') {
			$reader = New-Object System.IO.StreamReader($req.InputStream, [Text.Encoding]::UTF8)
			$body = $reader.ReadToEnd() | ConvertFrom-Json
			if ($null -ne $body.frame) { $state.frame = [double]$body.frame }
			if ($null -ne $body.fps -and [double]$body.fps -gt 0) { $state.fps = [double]$body.fps }
			if ($null -ne $body.playing) { $state.playing = [bool]$body.playing }
			if ($null -ne $body.speed -and [double]$body.speed -gt 0) { $state.speed = [double]$body.speed }
			$state.seq = $state.seq + 1
			$lastSync = Get-Date
			Send-Json $res @{ ok = $true; app = 'HA Video Reference'; version = $version }
		} elseif ($path -eq '/state') {
			$age = if ($null -eq $lastSync) { -1 } else { ((Get-Date) - $lastSync).TotalSeconds }
			Send-Json $res @{ frame = $state.frame; fps = $state.fps; playing = $state.playing; speed = $state.speed; seq = $state.seq; age = $age }
		} elseif ($path -eq '/ping') {
			Send-Json $res @{ ok = $true; app = 'HA Video Reference'; version = $version }
		} elseif ($path -eq '/' -or $path -eq '/index.html') {
			Send-Bytes $res ([IO.File]::ReadAllBytes($playerPath)) 'text/html; charset=utf-8'
		} else {
			Send-Json $res @{ ok = $false; error = 'not found' } 404
		}
	} catch {
		try { Send-Json $res @{ ok = $false; error = $_.Exception.Message } 500 } catch {}
	} finally {
		$res.Close()
	}
}
