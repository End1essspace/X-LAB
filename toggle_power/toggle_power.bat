@echo off
REM Switch between Balanced and High Performance power plans

REM Get the GUID of the active power scheme (4th token in the line)
for /f "tokens=4" %%G in ('powercfg /getactivescheme') do set CURR=%%G

echo Current GUID: %CURR%

REM Default GUIDs:
REM Balanced              381b4222-f694-41f0-9685-ff5bb260df2e
REM High performance      8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c

if /I "%CURR%"=="381b4222-f694-41f0-9685-ff5bb260df2e" (
    echo Switching to High performance...
    powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
) else (
    echo Switching to Balanced...
    powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e
)

echo.
powercfg /getactivescheme
pause
