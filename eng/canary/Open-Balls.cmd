@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "BALLS_PACKAGE=%~dp0"
set "BALLS_HOME=%LOCALAPPDATA%\Balls-Pilot"
set "BALLS_PIPE=balls-pilot"
set "BALLS_CLI=%BALLS_PACKAGE%balls\balls.exe"
set "BALLS_DAEMON=%BALLS_PACKAGE%ballsd\ballsd.exe"

if not exist "%BALLS_CLI%" goto missing_files
if not exist "%BALLS_DAEMON%" goto missing_files

"%BALLS_CLI%" --pipe-name "%BALLS_PIPE%" status >nul 2>&1
if not errorlevel 1 goto open_workspace

if not exist "%BALLS_HOME%\state" mkdir "%BALLS_HOME%\state"
if errorlevel 1 goto startup_failed

start "Balls background node" /min "%BALLS_DAEMON%" --data-directory "%BALLS_HOME%\state" --pipe-name "%BALLS_PIPE%" --node-name "%COMPUTERNAME%"

set /a BALLS_ATTEMPTS=30
:wait_for_node
"%BALLS_CLI%" --pipe-name "%BALLS_PIPE%" status >nul 2>&1
if not errorlevel 1 goto open_workspace
set /a BALLS_ATTEMPTS-=1
if %BALLS_ATTEMPTS% leq 0 goto startup_failed
ping -n 2 127.0.0.1 >nul
goto wait_for_node

:open_workspace
"%BALLS_CLI%" --pipe-name "%BALLS_PIPE%" ui
if errorlevel 1 goto startup_failed
exit /b 0

:missing_files
echo Extract the entire Balls download before opening this file.
pause
exit /b 1

:startup_failed
echo Balls could not start. Ask your Circle owner for help.
pause
exit /b 1
