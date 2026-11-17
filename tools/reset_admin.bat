@echo off
rem Forgot the admin password? Run this file ON THE SERVER PC.
rem Windows administrator approval (UAC) is required; then choose the admin account and set a new password.
rem (z2roN_Proj.exe --reset-admin works the same. It cannot be done from the web or from another PC.)
cd /d "%~dp0"
powershell -NoProfile -Command "Start-Process -FilePath '%~dp0z2roN_Proj.exe' -ArgumentList '--reset-admin' -Verb RunAs"
