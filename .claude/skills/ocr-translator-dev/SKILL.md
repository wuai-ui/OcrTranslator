---
name: ocr-translator-dev
description: 极客 OCR v4.0（WinUI 3 + .NET 10）项目开发规范与新增功能指南。触发：在本项目新增功能、修改代码、了解架构与规范时。
---

# 极客 OCR v4.0 开发指南

本 skill 指导如何在此项目中正确开发，避免重蹈已知坑。完整背景见仓库 `README.md`。

## 一、项目概览

- 技术栈：.NET 10 + WinUI 3 + Windows App SDK 1.7 + 未打包便携部署（`WindowsPackageType=None`，框架依赖）
- 架构：单项目内分层 + DI 容器 + 渐进式 MVVM（CommunityToolkit.Mvvm）
- 配置/日志：`%LOCALAPPDATA%\GeekOCR_v4`（与 v3 隔离）
- 目录：`Abstractions / Models / Services / Platform / ViewModels / Views / Styles`

## 二、★ 编译铁律（最重要）

**必须用 VS MSBuild，禁止 `dotnet build`**：
```bash
# 方式1：双击 build.cmd
# 方式2：命令行
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OcrTranslator.csproj -p:Configuration=Debug -p:Platform=x64
```

**原因**：`dotnet build` 的 XamlCompiler 遇 XAML 语义错误会**静默崩溃**（`MSB3073` 退出 1、无任何输出、不生成 output.json），极易误判为环境问题。VS MSBuild 才报真实 `WMCxxxx` 错误。

**exe 被占用**：`powershell.exe -Command "Stop-Process -Name OcrTranslator -Force"`（用户已授权直接 kill，不用问；勿用 `taskkill /F`，bash 会转义）。

## 三、代码规范

1. **C# nullable 启用**（`<Nullable>enable</Nullable>`）。新代码 nullable 安全；搬 v3 旧代码可加 `#nullable disable` 文件头零风险保真。
2. **命名空间分层**：`OcrTranslator.Abstractions/Models/Services/Platform/ViewModels/Views`。**Views 的 `x:Class` 必须与 cs namespace 完全一致**（不一致会触发 XamlCompiler 崩溃）。
3. **字符串字面量用单引号**（Windows 中文引号变异坑，见全局 CLAUDE.md）。
4. **DI 注入**：服务在 `App.xaml.cs` 的 `ConfigureServices()` 注册（接口→实现用单例，ViewModel 用瞬态）。View 在构造内 `InitializeComponent` **之前**从 `App.Services.GetService<T>()` 取服务（x:Bind 依赖此时序）。
5. **配置读写**：用 `SettingsService` 强类型属性（底层 `AppSettings`），勿直接散用 `AppSettings.Set`。
6. **写盘批量**：循环里调 `Set` 必须 `AppSettings.BeginBatch()`/`EndBatch()` 包裹（计数式，支持嵌套）。典型：保存字体列表、窗口关闭保存多个状态。
7. **用户提示用 InfoBar**（`ShowInfo(InfoBarSeverity, msg)`），**禁止把提示塞进译文/原文文本框**覆盖业务内容。
8. **颜色 token 化**：用 `Styles/Colors.xaml` 的 `{ThemeResource XxxBrush}`（亮/暗双色值），**禁止硬编码 `#FFxxxxxx`**。
9. **主题跟随系统**：`ThemeService` 管理三态（亮/暗/自动），勿在某元素强制 `RequestedTheme="Light"`。
10. **Win32 PInvoke 集中** `Platform/Win32/NativeMethods.cs`（截图/剪贴板/窗口前置）。**截图引擎 `Platform/CaptureWindow.cs` 保持纯 Win32 物理像素，勿改**（DPI 多屏兼容靠它）。
11. **安全**：API 密钥走配置（settings.json），**严禁硬编码**（v4 已清除 v3 的硬编码 AppID/SecretKey）。

## 四、如何新增功能（场景化）

### 场景 A：新增 OCR/翻译/TTS 引擎（如本地 PaddleOCR、DeepL、Edge-TTS）
1. `Services/` 新增实现类，实现 `IOcrEngine` / `ITranslator` / `ISpeechSynthesizer`
2. `App.xaml.cs` 的 `ConfigureServices()` 注册：`s.AddSingleton<IOcrEngine, PaddleOcrEngine>()`
3. 设置页加引擎选择 UI（存配置）
4. **无需改 View**（接口已解耦，这就是分层的好处）

### 场景 B：新增一个设置项
1. `SettingsService` 加属性（get/set 走 AppSettings，带默认值）
2. `Views/SettingsWindow.xaml` 加控件 + `.xaml.cs` 加 LoadSettings 读取 / SaveSettings_Click 保存
3. 保存路径若涉及多个 Set，用 `BeginBatch/EndBatch`
4. 主窗口需响应：`SettingsWindow.Closed` 回调里调 `ReloadSettingsFromLocal()`

### 场景 C：新增一个 UI 窗口（Window）
1. `Views/` 加 `XxxWindow.xaml(.cs)`：`x:Class="OcrTranslator.Views.XxxWindow"`，cs `namespace OcrTranslator.Views`
2. csproj 加：
   ```xml
   <None Remove="Views\XxxWindow.xaml" />
   <Page Update="Views\XxxWindow.xaml"><Generator>MSBuild:Compile</Generator></Page>
   ```
3. **用 VS MSBuild 编译**验证（dotnet 会静默崩）
4. 颜色用 token，主题 `_theme.ApplyTo(this.Content)`

### 场景 D：新增/改快捷键
- 录制逻辑在 `SettingsWindow.xaml.cs` 的 `HookCallback`（已支持单功能键 F1-F12 + 组合键，失败自动重置可继续录）
- 注册在 `HotkeyService.RegisterHotkey`（系统级 RegisterHotKey + 子类化）
- 默认值在 `SettingsService.OcrHotkey` / `TranslateHotkey`

### 场景 E：用 WinUI 控件属性（易踩坑）
- 不确定属性名 → **VS MSBuild 编译看 WMC 错误**
- `RadioButtons` 用 `MaxColumns`（不是 `Columns`）
- `MicaBackdrop` 不要设 `Kind`（直接 `new MicaBackdrop()`，`MicaKind` 在 WinAppSDK 1.7 不可用）
- `IServiceProvider.GetService<T>()` 需 `using Microsoft.Extensions.DependencyInjection`

## 五、常见坑速查

| 现象 | 原因 | 解决 |
|------|------|------|
| MSB3073 XamlCompiler 退出 1 无输出 | dotnet build 静默崩 | 换 VS MSBuild |
| exe 复制失败（文件锁定）| 程序运行中 | `powershell Stop-Process -Name OcrTranslator -Force` |
| 保存按钮卡顿 | 循环 Set 频繁写盘 | BeginBatch/EndBatch |
| 提示覆盖了译文 | `Vm.Text = msg` | 用 `ShowInfo` (InfoBar) |
| 截图 DPI 错位/模糊 | System.Drawing 自动缩放 | 用 Win32 BitBlt 物理像素（已实现）|
| x:Class 编译崩 | 与 cs namespace 不一致 | 两边统一命名空间 |
| 手动 XamlCompiler exit=1 无输出 | XAML 错被吞 | 换 VS MSBuild 看 WMC |
| PowerShell DrawString 报 PointF | Rectangle 重载解析 | 用 `RectangleF` |

## 六、目录约定

```
Abstractions/  接口（IOcrEngine/ITranslator/ISpeechSynthesizer/IBaiduTokenProvider）
Models/        数据（OcrMode/VoiceInfo/VoiceCatalog/AppTheme）
Services/      业务+平台服务（百度三引擎/Token/Settings/Theme/Hotkey/Clipboard/ScreenCapture/Startup/WindowPlacement/AppSettings/Logger/LanguageDetector）
Platform/      Win32（CaptureWindow 截图引擎 + Win32/NativeMethods）
ViewModels/    MVVM（MainViewModel，[ObservableProperty]/[RelayCommand]）
Views/         XAML 窗口（MainWindow/SettingsWindow/LoadingWindow）
Styles/        Colors/Controls/Fonts/Theme 资源字典
Assets/        图标(app.ico + 6 尺寸 PNG) + Lottie/GIF
```

## 七、提交与发布

```bash
git add . && git commit -m "<type>: <说明>" && git push origin main
```
- type：`feat`(新功能) / `fix`(修复) / `docs`(文档) / `refactor`(重构)
- 提交者：`wuai-ui <1258392094@qq.com>`（已配，勿改回旧值）

**发 Release**：
```bash
git tag v4.x
git push origin v4.x
```
GitHub Actions 自动编译 + 创建 Release + 附 x64 zip。

---

*遇到新坑请补充到本 skill 的「常见坑速查」，避免重复踩。*
