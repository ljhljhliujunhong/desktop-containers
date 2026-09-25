@echo off
chcp 65001 >nul
cd /d "%~dp0"
if not exist "%~dp0app\DesktopContainers.exe" (
  echo 还没有可运行的程序。请先按 docs\AI接手说明.md 发布到 app 目录。
  exit /b 1
)
start "" "%~dp0app\DesktopContainers.exe" %*
