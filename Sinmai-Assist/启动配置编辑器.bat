@echo off
chcp 65001 >nul
cd /d "%~dp0"
python config_editor_gui.py
pause
