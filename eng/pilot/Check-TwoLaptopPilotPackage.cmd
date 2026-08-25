@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "BALLS_ROOT=%~dp0Balls"
set "CANARY=%BALLS_ROOT%\canary.json"
set "CHECKSUMS=%BALLS_ROOT%\SHA256SUMS"
set "CLI=%BALLS_ROOT%\balls\balls.exe"
set "DAEMON=%BALLS_ROOT%\ballsd\ballsd.exe"
set "EXPECTED_COMMIT=67974f2de6502d99a55378e9da5aabf5e4293cc7"
set "MARKER_ROOT=%LOCALAPPDATA%\Balls-TwoLaptopPilot"
set "MARKER=%MARKER_ROOT%\package-path.txt"

if not exist "%CANARY%" goto incomplete
if not exist "%CHECKSUMS%" goto incomplete
if not exist "%CLI%" goto incomplete
if not exist "%DAEMON%" goto incomplete

findstr /L /C:"%EXPECTED_COMMIT%" "%CANARY%" >nul
if errorlevel 1 goto wrong_build

if not exist "%MARKER_ROOT%" mkdir "%MARKER_ROOT%"
if errorlevel 1 goto marker_failed
> "%MARKER%" echo "%BALLS_ROOT%"
if errorlevel 1 goto marker_failed

echo.
echo PASS checkpoint 1 - Balls is complete and ready at:
echo "%BALLS_ROOT%"
echo.
echo Leave this folder where it is and send this PASS result back to Codex.
pause
exit /b 0

:incomplete
echo.
echo BLOCKED - this pilot bundle is incomplete.
echo Right-click the downloaded ZIP, choose Extract All, then run this file
echo from the extracted folder. Do not run it inside the ZIP preview.
pause
exit /b 1

:wrong_build
echo.
echo BLOCKED - this is not the expected Balls pilot build.
echo Expected commit: %EXPECTED_COMMIT%
pause
exit /b 1

:marker_failed
echo.
echo BLOCKED - Balls could not record this package location for the next step.
echo Expected marker: "%MARKER%"
pause
exit /b 1
