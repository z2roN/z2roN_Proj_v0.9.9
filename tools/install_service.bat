@echo off
rem Run as Administrator. OPTIONAL headless mode: starts the server at boot WITHOUT any screen (no admin window).
rem Use install_autostart.bat instead if you want the server admin screen.
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo Please right-click this file and choose "Run as administrator".
  pause
  exit /b 1
)
set PORT=8080
set /p PORT=Port number (Enter = 8080, must match config.json): 
if "%PORT%"=="" set PORT=8080
netsh advfirewall firewall delete rule name="z2roN_Proj" >nul 2>&1
netsh advfirewall firewall add rule name="z2roN_Proj" dir=in action=allow protocol=TCP localport=%PORT%
schtasks /Delete /TN "z2roN_Proj" /F >nul 2>&1
schtasks /Create /TN "z2roN_Proj" /TR "\"%~dp0z2roN_Proj.exe\" --nogui" /SC ONSTART /RU SYSTEM /RL HIGHEST /F
schtasks /Run /TN "z2roN_Proj"
echo.
echo Done. Open http://localhost:%PORT% in a browser.
pause
