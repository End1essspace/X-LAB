@echo off
setlocal

cd /d "%~dp0"
call config.bat

echo.
echo ========================================
echo %PROJECT_NAME% - Create Release Tag
echo ========================================

if not exist "%DST%\.git" (
    echo ERROR: Destination is not a Git repository.
    pause
    exit /b 1
)

cd /d "%DST%"

echo.
git status

echo.
set /p VERSION=Enter version tag, example v1.2.0: 

if "%VERSION%"=="" (
    echo ERROR: Version cannot be empty.
    pause
    exit /b 1
)

echo.
echo Creating tag: %VERSION%
git tag -a %VERSION% -m "Release %VERSION%"

if errorlevel 1 (
    echo.
    echo ERROR: Tag creation failed.
    pause
    exit /b 1
)

echo.
set /p PUSH_TAG=Push tag to GitHub? Type YES: 

if /I "%PUSH_TAG%"=="YES" (
    git push origin %VERSION%

    if errorlevel 1 (
        echo.
        echo ERROR: Tag push failed.
        pause
        exit /b 1
    )

    echo.
    echo Tag pushed.
) else (
    echo.
    echo Tag created locally but not pushed.
)

pause
exit /b 0