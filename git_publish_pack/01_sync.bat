@echo off
setlocal

cd /d "%~dp0"
call config.bat

echo.
echo ========================================
echo %PROJECT_NAME% - Sync Working Folder
echo ========================================
echo Source:      %SRC%
echo Destination: %DST%
echo Backup:      %BACKUP_DIR%
echo.

if not exist "%SRC%" (
    echo ERROR: Source folder does not exist.
    pause
    exit /b 1
)

if not exist "%DST%\.git" (
    echo ERROR: Destination is not a Git repository.
    echo Expected: %DST%\.git
    pause
    exit /b 1
)

if not exist "%BACKUP_DIR%" (
    mkdir "%BACKUP_DIR%"
)

echo This will create a backup of the GitHub folder before syncing.
echo.
set /p CONFIRM=Continue sync? Type YES: 

if /I not "%CONFIRM%"=="YES" (
    echo Cancelled.
    pause
    exit /b 0
)

echo.
echo Creating backup...

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HH-mm-ss"') do set "TIMESTAMP=%%i"

set "BACKUP_FILE=%BACKUP_DIR%\%PROJECT_NAME%_%TIMESTAMP%.zip"

powershell -NoProfile -Command ^
"Compress-Archive -Path '%DST%\*' -DestinationPath '%BACKUP_FILE%' -Force"

if errorlevel 1 (
    echo ERROR: Backup failed.
    pause
    exit /b 1
)

echo Backup created:
echo %BACKUP_FILE%

echo.
echo Syncing files...

robocopy "%SRC%" "%DST%" /E /XD %EXCLUDE_DIRS% /XF %EXCLUDE_FILES%

set "ROBOCOPY_EXIT=%ERRORLEVEL%"

REM Robocopy codes 0-7 are usually successful/non-critical.
if %ROBOCOPY_EXIT% GEQ 8 (
    echo.
    echo ERROR: Robocopy failed with code %ROBOCOPY_EXIT%.
    pause
    exit /b %ROBOCOPY_EXIT%
)

echo.
echo Sync completed successfully.
pause
exit /b 0