@echo off
rem Ferma completamente NAM Trainer (server + eventuali training in corso).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0nam-web\stop.ps1"
timeout /t 4 >nul
