@echo off
setlocal

cd /d "%~dp0"
call config.bat

echo.
echo ========================================
echo %PROJECT_NAME% - Full Publish
echo ========================================
echo Source:      %SRC%
echo Destination: %DST%
echo Branch:      %BRANCH%
echo.

if not exist "%SRC%" (
    echo ERROR: Source folder does not exist.
    pause
    exit /b 1
)

if not exist "%DST%\.git" (
    echo ERROR: Destination is not a Git repository.
    pause
    exit /b 1
)

if not exist "%BACKUP_DIR%" (
    mkdir "%BACKUP_DIR%"
)

echo This will:
echo 1. Backup the GitHub repo folder
echo 2. Sync working folder to GitHub folder
echo 3. Show git status
echo 4. Create commit
echo 5. Optionally push to GitHub
echo.
set /p CONFIRM=Start full publish? Type YES: 

if /I not "%CONFIRM%"=="YES" (
    echo Cancelled.
    pause
    exit /b 0
)

echo.
echo ========================================
echo Step 1: Backup
echo ========================================

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
echo ========================================
echo Step 2: Sync
echo ========================================

robocopy "%SRC%" "%DST%" /E /XD %EXCLUDE_DIRS% /XF %EXCLUDE_FILES%

set "ROBOCOPY_EXIT=%ERRORLEVEL%"

if %ROBOCOPY_EXIT% GEQ 8 (
    echo.
    echo ERROR: Robocopy failed with code %ROBOCOPY_EXIT%.
    pause
    exit /b %ROBOCOPY_EXIT%
)

echo.
echo Sync completed.

cd /d "%DST%"

echo.
echo ========================================
echo Step 3: Git Status
echo ========================================
git status

echo.
set /p MSG=Enter commit message: 

if "%MSG%"=="" (
    echo ERROR: Commit message cannot be empty.
    pause
    exit /b 1
)

echo.
echo ========================================
echo Step 4: Commit
echo ========================================

git add .
git commit -m "%MSG%"

if errorlevel 1 (
    echo.
    echo WARNING: Commit failed or nothing to commit.
    pause
    exit /b 1
)

echo.
echo ========================================
echo Step 5: Push
echo ========================================

set /p PUSH=Push to origin/%BRANCH% now? Type YES: 

if /I "%PUSH%"=="YES" (
    git push origin %BRANCH%

    if errorlevel 1 (
        echo.
        echo ERROR: Push failed.
        pause
        exit /b 1
    )

    echo.
    echo Push completed.
) else (
    echo.
    echo Commit created locally, but not pushed.
)

echo.
echo Full publish completed.
pause
exit /b 0