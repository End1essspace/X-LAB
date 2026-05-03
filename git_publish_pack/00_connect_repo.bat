@echo off
setlocal

cd /d "%~dp0"
call config.bat

echo.
echo ========================================
echo %PROJECT_NAME% - Connect Existing GitHub Repo
echo ========================================
echo Source:      %SRC%
echo Destination: %DST%
echo Branch:      %BRANCH%
echo Remote URL:  %REMOTE_URL%
echo.

if not exist "%SRC%" (
    echo ERROR: Source folder does not exist.
    echo %SRC%
    pause
    exit /b 1
)

where git >nul 2>nul
if errorlevel 1 (
    echo ERROR: Git is not installed or not available in PATH.
    pause
    exit /b 1
)

if "%REMOTE_URL%"=="" (
    echo ERROR: REMOTE_URL is empty in config.bat.
    pause
    exit /b 1
)

echo This script will:
echo 1. Create destination folder if it does not exist
echo 2. Initialize Git in destination folder if needed
echo 3. Copy project files from SRC to DST
echo 4. Create .gitignore if missing
echo 5. Create initial commit if needed
echo 6. Connect remote origin
echo 7. Push to GitHub
echo.
set /p CONFIRM=Continue? Type YES: 

if /I not "%CONFIRM%"=="YES" (
    echo Cancelled.
    pause
    exit /b 0
)

if not exist "%DST%" (
    echo.
    echo Creating destination folder...
    mkdir "%DST%"
)

cd /d "%DST%"

if not exist "%DST%\.git" (
    echo.
    echo Initializing Git repository...
    git init
)

echo.
echo Setting branch to %BRANCH%...
git branch -M %BRANCH%

echo.
echo Syncing project files...

robocopy "%SRC%" "%DST%" /E /XD %EXCLUDE_DIRS% /XF %EXCLUDE_FILES%

set "ROBOCOPY_EXIT=%ERRORLEVEL%"

if %ROBOCOPY_EXIT% GEQ 8 (
    echo.
    echo ERROR: Robocopy failed with code %ROBOCOPY_EXIT%.
    pause
    exit /b %ROBOCOPY_EXIT%
)

if not exist "%DST%\.gitignore" (
    echo.
    echo Creating default .gitignore...

    (
        echo # System
        echo Thumbs.db
        echo desktop.ini
        echo.
        echo # Logs / temp
        echo *.log
        echo *.tmp
        echo *.cache
        echo *.bak
        echo.
        echo # Python
        echo __pycache__/
        echo *.pyc
        echo .venv/
        echo venv/
        echo env/
        echo.
        echo # Java / Gradle
        echo .gradle/
        echo build/
        echo out/
        echo target/
        echo.
        echo # .NET
        echo bin/
        echo obj/
        echo.
        echo # Node
        echo node_modules/
        echo dist/
        echo.
        echo # IDE
        echo .idea/
        echo .vscode/
    ) > "%DST%\.gitignore"
)

echo.
echo Current Git status:
echo.
git status

echo.
set /p MSG=Enter initial commit message or press Enter for default: 

if "%MSG%"=="" (
    set "MSG=chore: initial commit"
)

echo.
echo Creating initial commit if needed...
git add .
git commit -m "%MSG%"

if errorlevel 1 (
    echo.
    echo WARNING: Commit failed or nothing to commit.
    echo Continuing to remote setup...
)

echo.
echo Checking remote origin...

git remote get-url origin >nul 2>nul

if errorlevel 1 (
    echo Adding remote origin...
    git remote add origin "%REMOTE_URL%"
) else (
    echo Remote origin already exists.
    echo Updating origin URL...
    git remote set-url origin "%REMOTE_URL%"
)

echo.
echo Remote list:
git remote -v

echo.
set /p PUSH=Push to origin/%BRANCH% now? Type YES: 

if /I "%PUSH%"=="YES" (
    git push -u origin %BRANCH%

    if errorlevel 1 (
        echo.
        echo ERROR: Push failed.
        echo.
        echo Possible reason:
        echo - GitHub repository already has README/LICENSE/.gitignore commit.
        echo - Remote branch has history that local repo does not have.
        echo.
        echo Try manually:
        echo git pull origin %BRANCH% --allow-unrelated-histories
        echo then resolve if needed, then push again.
        pause
        exit /b 1
    )

    echo.
    echo First push completed.
) else (
    echo.
    echo Local repo connected, but not pushed.
)

echo.
echo Repository connected successfully.
pause
exit /b 0