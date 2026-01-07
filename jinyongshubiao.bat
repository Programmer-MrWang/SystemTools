@echo off
:: 检查并请求管理员权限
net session >nul 2>&1
if %errorLevel% NEQ 0 (
    powershell -Command "Start-Process -FilePath '%~0' -Verb RunAs"
    exit /b
)

:: 以管理员身份隐藏运行 PowerShell 脚本
powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0jinyongshubiao.ps1"