@echo off

REM ================================
REM X-LAB Git Publish Pack Config
REM ================================

set "PROJECT_NAME=YourProject"

set "SRC=D:\projects\MP\YourProject"
set "DST=D:\projects\GitHub\YourProject"
set "BACKUP_DIR=D:\projects\Backups\YourProject"

set "BRANCH=main"
set "REMOTE_URL=https://github.com/YourUsername/YourProject.git"

set "EXCLUDE_DIRS=.git .venv venv env __pycache__ build dist out target bin obj .idea .vscode node_modules .gradle"
set "EXCLUDE_FILES=*.pyc *.pyo *.log *.tmp *.cache *.bak Thumbs.db desktop.ini"