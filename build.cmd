@echo off
rem Builds AtasStrategies.dll and copies it to the ATAS Strategies folder. See build.ps1 for options.
rem Examples:  build.cmd          build.cmd -Test
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
exit /b %ERRORLEVEL%
