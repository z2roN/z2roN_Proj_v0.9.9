@echo off
rem Run as Administrator. Stops the server and removes the auto-start task and firewall rule. Data is kept.
net session >nul 2>&1
if errorlevel 1 (
  echo Please right-click this file and choose "Run as administrator".
  pause
  exit /b 1
)
schtasks /End /TN "z2roN_Proj" >nul 2>&1
schtasks /Delete /TN "z2roN_Proj" /F
taskkill /IM z2roN_Proj.exe /F >nul 2>&1
netsh advfirewall firewall delete rule name="z2roN_Proj"
echo Done.
pause
