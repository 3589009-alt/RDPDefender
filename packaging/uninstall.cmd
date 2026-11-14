@echo off
setlocal
cd /d "%~dp0"

echo.
echo  RDP Defender - service removal
echo  ------------------------------
echo.
echo Settings and logs are kept in %%ProgramData%%\RDP Defender.
echo To remove them too, run: RdpDefender.exe --uninstall --purge
echo.

"%~dp0RdpDefender.exe" --uninstall
if errorlevel 1 (
    echo.
    echo Removal failed. Make sure you have administrator rights.
    pause
    exit /b 1
)

echo.
echo The RDP Defender service has been removed.
echo.
pause
