@echo off
setlocal
set "SCRIPT=%~dp0Initialize-BallsDevLink.ps1"
set "PUBLIC_KEY=%~dp0Balls-Dev-Link.pub"

if not exist "%SCRIPT%" (
  echo Missing %SCRIPT%
  exit /b 1
)
if not exist "%PUBLIC_KEY%" (
  echo Missing %PUBLIC_KEY%
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%SCRIPT%" -Action Configure -AuthorizedKeyPath "%PUBLIC_KEY%" -ConfirmSystemChange
if errorlevel 1 pause
