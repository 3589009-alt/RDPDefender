@echo off
setlocal
cd /d "%~dp0"

echo.
echo  RDP Defender - service installation
echo  -----------------------------------
echo.

"%~dp0RdpDefender.exe" --install
if errorlevel 1 (
    echo.
    echo Installation failed. Make sure you have administrator rights.
    pause
    exit /b 1
)

echo.
echo The RDP Defender service is installed and started.
echo Open RdpDefender.exe to configure protection.
echo.
pause
