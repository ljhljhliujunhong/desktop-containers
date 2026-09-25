@echo off
chcp 65001 >nul
cd /d "%~dp0"
if not exist "%~dp0澄格.exe" (
  echo 还没有可运行的程序。请先按 docs\AI接手说明.md 发布到项目根目录的 澄格.exe。
  exit /b 1
)
start "" "%~dp0澄格.exe" %*
