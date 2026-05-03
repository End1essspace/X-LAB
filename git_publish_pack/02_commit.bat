@echo off
setlocal

cd /d "%~dp0"
call config.bat

echo.
echo ========================================
echo %PROJECT_NAME% - Commit Changes
echo ========================================

if not exist "%DST%\.git" (
    echo ERROR: Destination is not a Git repository.
    pause
    exit /b 1
)

cd /d "%DST%"

echo.
echo Current Git status:
echo.
git status

echo.
set /p MSG=Enter commit message: 

if "%MSG%"=="" (
    echo ERROR: Commit message cannot be empty.
    pause
    exit /b 1
)

echo.
echo Adding files...
git add .

echo.
echo Creating commit...
git commit -m "%MSG%"

if errorlevel 1 (
    echo.
    echo WARNING: Commit failed or there is nothing to commit.
    pause
    exit /b 1
)

echo.
echo Commit completed.
pause
exit /b 0