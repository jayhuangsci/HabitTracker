@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0scripts\Start-PromptCrew.ps1" %*
set "promptcrewExitCode=%ERRORLEVEL%"
if not "%promptcrewExitCode%"=="0" pause
exit /b %promptcrewExitCode%
