@echo off
rem Double-click to stop a BLADE server and app that were left running.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\start-demo.ps1" -Stop
