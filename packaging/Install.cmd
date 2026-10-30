@echo off
setlocal
rem  Automation Monitor - entry point for Windows Explorer (double-click).
rem  Uses Windows PowerShell 5.1, which ships with Windows 10 and 11.
rem  -ExecutionPolicy Bypass applies to this process only; nothing system-wide is changed
rem  and no administrator rights are requested.
rem  Unattended use (CI or scripts): set AM_NO_PAUSE=1 so this window does not wait for a key.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
set "AM_INSTALL_EXITCODE=%ERRORLEVEL%"
if not "%AM_NO_PAUSE%"=="1" pause
exit /b %AM_INSTALL_EXITCODE%
