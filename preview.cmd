@echo off
rem Double-click launcher for preview.ps1 (e.g. preview.cmd doc)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0preview.ps1" %*
