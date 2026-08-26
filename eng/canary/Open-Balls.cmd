@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "BALLS_PACKAGE=%~dp0"
set "BALLS_HOME=%LOCALAPPDATA%\Balls-Pilot"
set "BALLS_PIPE=balls-pilot"
set "BALLS_CLI=%BALLS_PACKAGE%balls\balls.exe"
set "BALLS_DAEMON=%BALLS_PACKAGE%ballsd\ballsd.exe"
set "BALLS_DAEMON_DIRECTORY=%BALLS_PACKAGE%ballsd"
set "BALLS_STATE=%BALLS_HOME%\state"
set "BALLS_LOGS=%BALLS_HOME%\logs"
set "BALLS_STDOUT=%BALLS_LOGS%\ballsd.stdout.log"
set "BALLS_STDERR=%BALLS_LOGS%\ballsd.stderr.log"
set "BALLS_DAEMON_ARGUMENTS=--data-directory "%BALLS_STATE%" --pipe-name "%BALLS_PIPE%" --node-name "%COMPUTERNAME%""

if not exist "%BALLS_CLI%" goto missing_files
if not exist "%BALLS_DAEMON%" goto missing_files

"%BALLS_CLI%" --pipe-name "%BALLS_PIPE%" status >nul 2>&1
if not errorlevel 1 goto open_workspace

if not exist "%BALLS_STATE%" mkdir "%BALLS_STATE%"
if errorlevel 1 goto startup_failed
if not exist "%BALLS_LOGS%" mkdir "%BALLS_LOGS%"
if errorlevel 1 goto startup_failed

powershell.exe -NoLogo -NoProfile -NonInteractive -Command ^
  "try { Start-Process -FilePath $env:BALLS_DAEMON -ArgumentList $env:BALLS_DAEMON_ARGUMENTS -WorkingDirectory $env:BALLS_DAEMON_DIRECTORY -WindowStyle Hidden -RedirectStandardOutput $env:BALLS_STDOUT -RedirectStandardError $env:BALLS_STDERR -ErrorAction Stop; exit 0 } catch { $_ | Out-String | Set-Content -LiteralPath $env:BALLS_STDERR; exit 1 }"
if errorlevel 1 goto startup_failed

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
if errorlevel 1 goto workspace_failed
exit /b 0

:missing_files
echo Extract the entire Balls download before opening this file.
pause
exit /b 1

:startup_failed
echo Balls could not start. Ask your Circle owner for help.
echo Startup log: %BALLS_STDERR%
if exist "%BALLS_STDERR%" type "%BALLS_STDERR%"
pause
exit /b 1

:workspace_failed
echo Balls is running, but its workspace could not open.
echo Try this file again. If it still fails, ask your Circle owner for help.
pause
exit /b 1
