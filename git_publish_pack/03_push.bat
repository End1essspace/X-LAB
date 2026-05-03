@echo off
setlocal

cd /d "%~dp0"
call config.bat

echo.
echo ========================================
echo %PROJECT_NAME% - Push to GitHub
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
set /p CONFIRM=Push to origin/%BRANCH%? Type YES: 

if /I not "%CONFIRM%"=="YES" (
    echo Cancelled.
    pause
    exit /b 0
)

echo.
git push origin %BRANCH%

if errorlevel 1 (
    echo.
    echo ERROR: Push failed.
    pause
    exit /b 1
)

echo.
echo Push completed.
pause
exit /b 0