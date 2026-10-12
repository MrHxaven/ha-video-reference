@echo off
title HA Video Reference
rem Starts the HA Video Reference app (a local server on this computer only) and opens the player in your browser.
rem Optional: a different port, e.g.  "Start Video Reference.bat" 34901
set PORT=%1
if "%PORT%"=="" set PORT=34900
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0server.ps1" -Port %PORT%
