@echo off
setlocal
REM 自动定位 Visual Studio 自带的 MSBuild（用微软官方 vswhere 工具）
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe"`) do set "MSBUILD=%%i"

if not defined MSBUILD (
    echo [ERROR] 未找到 Visual Studio。请安装 VS 2022 或更新版本（勾选「.NET 桌面开发」工作负载）。
    pause & exit /b 1
)

echo [v4.0 编译] 使用 MSBuild:
echo   %MSBUILD%
echo.
"%MSBUILD%" "%~dp0OcrTranslator.csproj" -p:Configuration=Debug -p:Platform=x64 -clp:ErrorsOnly
echo.
if %errorlevel%==0 (
    echo [OK] 编译成功
    echo exe 路径: %~dp0bin\x64\Debug\net10.0-windows10.0.19041.0\OcrTranslator.exe
) else (
    echo [FAILED] 编译有错误，见上方输出
)
pause
