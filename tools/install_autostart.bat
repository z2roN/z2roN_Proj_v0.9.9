@echo off
rem Run as Administrator. RECOMMENDED. Opens the firewall port and starts z2roN_Proj (with the admin screen in the tray)
rem every time this user logs in to Windows. For a server PC, also enable Windows auto sign-in (netplwiz).
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
schtasks /Create /TN "z2roN_Proj" /TR "\"%~dp0z2roN_Proj.exe\" --tray" /SC ONLOGON /RL HIGHEST /F
echo.
echo Done. Starting the server now...
start "" "%~dp0z2roN_Proj.exe"
pause
