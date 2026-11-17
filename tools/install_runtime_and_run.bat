@echo off
chcp 65001 >nul
rem ============================================================
rem  z2roN_Proj 경량판 - 필요한 .NET 런타임이 없으면 자동으로 내려받아 설치한 뒤 실행합니다.
rem   - 필요: .NET Desktop Runtime 8 (x64) + ASP.NET Core Runtime 8 (x64)
rem   - 인터넷 연결과 관리자 권한이 필요합니다 (권한이 없으면 승인 창이 뜹니다).
rem   - 이미 설치되어 있으면 설치 없이 바로 실행합니다.
rem ============================================================
cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
  echo 관리자 권한으로 다시 실행합니다...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

set "NEED_DESK=0"
set "NEED_ASP=0"
dir /b "%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\8.*" >nul 2>&1 || set "NEED_DESK=1"
dir /b "%ProgramFiles%\dotnet\shared\Microsoft.AspNetCore.App\8.*" >nul 2>&1 || set "NEED_ASP=1"

if "%NEED_DESK%"=="1" call :install "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe" "%TEMP%\z2ron_desktop_runtime.exe" ".NET Desktop Runtime 8"
if "%NEED_ASP%"=="1"  call :install "https://aka.ms/dotnet/8.0/aspnetcore-runtime-win-x64.exe" "%TEMP%\z2ron_aspnet_runtime.exe" "ASP.NET Core Runtime 8"

echo.
echo z2roN_Proj 를 실행합니다.
start "" "%~dp0z2roN_Proj.exe"
exit /b 0

:install
echo.
echo [%~3] 설치 파일을 내려받는 중... (%~1)
powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; try { Invoke-WebRequest -UseBasicParsing -Uri '%~1' -OutFile '%~2' } catch { exit 1 }"
if errorlevel 1 goto :fail
echo [%~3] 설치 중... (1~2분 걸릴 수 있습니다)
"%~2" /install /quiet /norestart
rem 0 = 성공, 3010 = 성공(재부팅 권장)
if not "%errorlevel%"=="0" if not "%errorlevel%"=="3010" goto :fail
del "%~2" >nul 2>&1
echo [%~3] 설치 완료
goto :eof

:fail
echo.
echo 자동 설치에 실패했습니다. 아래 주소에서 x64 설치 파일 2개를 직접 설치하세요.
echo   https://dotnet.microsoft.com/download/dotnet/8.0
echo   (1) .NET Desktop Runtime 8   (2) ASP.NET Core Runtime 8
pause
exit /b 1
