# 极客 OCR v4.0 重构技术文档

> 从 v3.0 到 v4.0：架构现代化（分层 + DI + MVVM）与 UI 前沿化（Mica + 三态主题 + Token 化）的完整重构记录。本文档沉淀全过程的问题、思路、方案与产物，供后续维护与同类项目参考。

---

## 一、项目背景与重构目标

### 1.1 重构动机

v3.0 的技术栈选型已经是前沿（`.NET 10` + `WinUI 3` + `Windows App SDK 1.7` + 未打包便携部署），但存在三个核心问题：

| 维度 | v3 现状 | 问题 |
|------|---------|------|
| UI 主题 | 强制 `RequestedTheme="Light"` | 违背 2026「跟随系统暗色」主流 |
| UI 材质 | 无 Mica/Acrylic，纯白卡片 | 没用上 Win11 招牌视觉 |
| 样式管理 | 全部内联，硬编码 `#FFxxxxxx` | 无法换肤、无法适配暗色 |
| 架构 | 无 MVVM、无 DI、过程式 + 全 `static` | `MainWindow.xaml.cs` 沦为 861 行上帝类 |
| API 层 | `BaiduApi` 硬耦合百度 | 换引擎需大改 |
| 安全 | `BaiduApi.cs` 硬编码翻译密钥 | 泄露风险 |

**结论**：框架选型已最优，问题在「形似而神不至」——升级的本质是**兑现框架的现代能力**，而非推倒重来。截图引擎（纯 Win32 分层窗口）是优秀实现，应保留。

### 1.2 目标

在 `OcrTranslator_v4.0` 新建项目，**100% 保留全部现有功能**，仅升级架构与 UI。不动 v3.0/v2.0 等任何现有文件。

### 1.3 已确认决策

1. 主题：**跟随系统 + 三态切换**（亮 / 暗 / 自动）
2. 架构：**单项目内命名空间分层**（不拆类库，沿用未打包部署稳定性）
3. 范围：**纯重构 + 架构前瞻**（接口抽象铺路，不新增引擎）

---

## 二、技术选型与架构

### 2.1 目标目录结构

```
OcrTranslator_v4.0\
├── OcrTranslator.csproj      # v3 风格 csproj + MVVM/DI 包
├── app.manifest              # PerMonitorV2 DPI
├── Program.cs                # Bootstrap 显式入口
├── App.xaml(.cs)             # DI 容器 + 主题初始化
├── Abstractions/             # IOcrEngine / ITranslator / ISpeechSynthesizer / IBaiduTokenProvider
├── Models/                   # OcrMode / VoiceInfo / VoiceCatalog / AppTheme
├── Services/                 # 百度三引擎 + Token + 设置 + 主题 + 热键 + 剪贴板 + 截图 + 自启 + 窗口位置
├── Platform/                 # CaptureWindow（纯 Win32 截图）+ Win32/NativeMethods
├── ViewModels/               # MainViewModel（CommunityToolkit.Mvvm）
├── Views/                    # MainWindow / SettingsWindow / LoadingWindow
└── Styles/                   # Colors / Controls / Fonts / Theme
```

### 2.2 接口抽象（解耦百度，为换引擎铺路）

```csharp
IOcrEngine.RecognizeAsync(byte[] image, OcrMode mode) → string
ITranslator.TranslateAsync(string text, string toLang) → string
ISpeechSynthesizer.SynthesizeAsync(string text, VoiceInfo voice, ...) → byte[]
IBaiduTokenProvider          # OCR/Voice token 双检锁缓存
```

未来插 PaddleOCR 本地引擎、DeepL、Edge-TTS 只需新增实现类，DI 注册切换。

### 2.3 依赖注入

`App.xaml.cs` 建 `IServiceProvider`，挂 `App.Services` 静态属性。三个引擎接口 → 百度实现（单例），平台服务单例，ViewModel 瞬态。`OnLaunched` 解析 MainWindow。

### 2.4 渐进式 MVVM（CommunityToolkit.Mvvm）

**分工原则：可测试的纯状态/业务进 ViewModel，平台互操作留 View。**

- `MainViewModel`：`[ObservableProperty]` 文本/耗时/朗读状态 + `[RelayCommand]` 翻译
- View code-behind：截图 OCR 工作流、热键、剪贴板、TTS 播放、滚动同步（这些是 Win32/MediaPlayer 互操作）

---

## 三、UI 现代化方案

### 3.1 Mica 云母材质

```csharp
SystemBackdrop = new MicaBackdrop();  // 主窗口/设置窗口
SystemBackdrop = new DesktopAcrylicBackdrop();  // Loading 弹层（Glassmorphism 2.0）
```

### 3.2 三态主题切换

`ThemeService` 维护多窗口根元素列表，`Apply(theme)` 在所有根元素设 `RequestedTheme`（WinUI 运行时即时生效，无需重启）。持久化到 settings。

### 3.3 样式 Token 化

`Styles/Colors.xaml` 用 `ThemeDictionaries` 定义亮/暗双色值（AccentBrush / CardBackgroundBrush / TextPrimaryBrush 等），`Controls.xaml` 定义命名样式，**消灭所有硬编码 `#FFxxxxxx`**。

---

## 四、★ 核心问题与解决方案

### 4.1 dotnet CLI XamlCompiler 静默崩溃（最大坑，耗时约 2 小时）

**现象**：`dotnet build` 编译 v4，报：
```
error MSB3073: 命令"XamlCompiler.exe input.json output.json"已退出，代码为 1
```
**无任何错误输出，不生成 output.json。** 极易误判为环境/打包/SDK 版本问题。

**错误排查路径（全部排除）**：
- ✗ x:Bind 绑定 ViewModel 属性 → 移除仍崩
- ✗ App.xaml 引用外部 ResourceDictionary（ms-appx）→ 移除仍崩
- ✗ XAML 子文件夹 → 根目录仍崩
- ✗ x:Class 子命名空间 → 根命名空间仍崩
- ✗ CommunityToolkit.Mvvm / DI 包 → 注释仍崩
- ✗ csproj 的 None Remove / Page Update → 加了仍崩
- ✗ 对比 v3/v4 的 input.json → 结构完全一致

**转折点**：用户提示看历史文档，发现 **「dotnet CLI 不含 WinUI 3 的 PRI 编译任务」**。改用 **VS MSBuild** 编译，立刻报出真错：
```
XamlCompiler error WMC0011: Unknown member 'Columns' on element 'RadioButtons'
```

**根因**：`dotnet build` 自带的精简版 MSBuild + XamlCompiler，遇到 XAML 语义错误时**不报告而是直接崩溃**。只有 VS 完整 MSBuild 才正确报 `WMCxxxx` 错误。v3 能用 dotnet build 是因为它的 XAML 恰好无错。

**解决方案**：WinUI 3 项目**必须用 VS MSBuild 编译**：
```
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" 项目.csproj -p:Configuration=Debug -p:Platform=x64
```
用 `vswhere` 自动定位。本项目提供 `build.cmd` 双击编译。

**教训**：手动运行 `XamlCompiler.exe input.json output.json` 若 `exit=1` 无输出，几乎肯定是 XAML 错误被 dotnet 吞了 → 换 VS MSBuild。

### 4.2 XAML/C# 编译错误（VS MSBuild 暴露后的批量修复）

| 错误 | 根因 | 修复 |
|------|------|------|
| `RadioButtons.Columns` 不存在 (WMC0011) | WinUI 控件属性名错 | 改 `MaxColumns` |
| `MicaKind` 命名空间不可用 (CS0234) | WinAppSDK 1.7 该版本 `MicaBackdrop.Kind` 不可用 | `new MicaBackdrop()`（不设 Kind）|
| `IServiceProvider.GetService<T>` 非泛型 (CS0308) | 缺扩展方法 using | 加 `using Microsoft.Extensions.DependencyInjection` |
| `HttpClientHandler.Timeout` 不存在 (CS0117) | Timeout 是 HttpClient 的属性 | `new HttpClient { Timeout = ... }` |
| `JsonElement.ArrayEnumerator.Select` 缺失 (CS1061) | 缺 Linq | 加 `using System.Linq` |
| `MainViewModel.TranslateAsync` 不可访问 (CS0122) | `[RelayCommand] private` 生成私有 | 改调 public `TranslateTextAsync` |
| `IReadOnlyList<>` 找不到 (CS0246) | 缺 using | 加 `using System.Collections.Generic` |
| `x:Class` 与 cs namespace 不匹配 | 改 x:Class 没同步改 cs | 两边统一命名空间 |

### 4.3 写盘卡顿 → 计数式批量

**现象**：点保存按钮卡一下；用户要求全面排查写盘。

**排查**：grep 所有 `AppSettings.Set` / `SettingsService` setter，定位写盘点：

| 位置 | 写盘次数 | 原因 |
|------|---------|------|
| SaveToggleSettings（字体循环）| 上百次 | 每个字体 toggle 一次写盘 |
| MainWindow 关闭 | 9 次 | 5 下拉框 + 4 窗口位置 |
| SettingsWindow 关闭 | 4 次 | 窗口位置 |
| WindowPlacementService.Save | 4 次 | 通用方法 |

**根因**：`AppSettings.Set` 每次都 `Save()`（整个 JSON 序列化写盘）。

**解决方案**：
1. AppSettings 加**计数式批量**（支持嵌套，内外层不冲突）：
```csharp
private static int _batchDepth;
public static void BeginBatch() { _batchDepth++; }
public static void EndBatch() { if (_batchDepth > 0 && --_batchDepth == 0) Save(); }
private static void Save() { if (_batchDepth > 0) return; ... }
```
2. 所有保存点包裹 `BeginBatch/EndBatch`（含 `WindowPlacementService.Save` 内部自带，任何调用方都安全）。

**效果**：字体上百次写盘 → 1 次；MainWindow 关闭 9 次 → 1 次。

### 4.4 下载提示覆盖译文 → InfoBar

**问题**：下载成功/失败用 `Vm.TranslatedText = "..."` 显示，覆盖译文。

**方案评估**：

| 方案 | 侵入性 | 成本 | 结论 |
|------|--------|------|------|
| InfoBar（WinUI 横幅）| 非侵入、自动消失 | 中 | ✅ 选此 |
| 底部状态栏 | 占常驻空间 | 低 | 占行 |
| ContentDialog | 弹窗打断 | 低 | 太重 |
| TeachingTip | 气泡指向控件 | 高 | 复杂 |

**实现**：MainWindow 底部加 `InfoBar`（Grid.Row=3，折叠时不占空间），`ShowInfo(severity, message)` 显示后 3.5 秒自动关。下载成功/失败、翻译空、朗读空、音频错误**全部走 InfoBar**，译文区永不被覆盖。

---

## 五、其他问题与修复

### 5.1 翻译慢 → 分段并行

`SplitByLanguage` 把中英混合文本分段，原串行 `foreach await`（每段一次 HTTP 往返）。改 `Task.WhenAll` 并行：
```csharp
var tasks = segments.Select(seg => seg.isTargetLang
    ? Task.FromResult(seg.text)
    : TranslateRawAsync(seg.text, "auto", toLang, ct)).ToArray();
return string.Concat(await Task.WhenAll(tasks));
```
多段时大幅加速。注：单段纯文本仍受百度 API 网络延迟限制（不可控）。

### 5.2 译文框不可编辑 → TwoWay

去掉 `IsReadOnly="True"`，绑定改 `Mode=TwoWay, UpdateSourceTrigger=PropertyChanged`。

### 5.3 快捷键录入

- 允许**单功能键**（F1-F12、PrintScreen、Pause 等）独立录制，不强制辅助键
- 修复按错卡死：失败时重置修饰键状态 + 不停止录制，可继续按

### 5.4 设置页 TAB 重排 + 快捷键独立

新顺序：① 系统设置 → ② API → ③ 快捷键 → ④ OCR 模式 → ⑤ 字体 → ⑥ 关于。快捷键从系统设置拆出独立 TAB。

### 5.5 左下角空白设置按钮

NavigationView 内置 Settings 齿轮（没绑定内容）。设 `IsSettingsVisible="False"` 隐藏。

### 5.6 按钮颜色

- 置顶按钮：去掉强制灰色 Foreground，ToggleButton 按下态自动反色
- 左/右字体按钮：颜色改与下拉框一致（继承默认）

### 5.7 启动焦点

首次激活时焦点到原文输入框（`FocusState.Programmatic`），不再落在右上角主题按钮。

### 5.8 设置页 caption 按钮

foreground 跟随主题（亮深暗白）+ hover 半透明背景，主题切换时 `UpdateCaptionColors()` 同步。

### 5.9 软件图标

PowerShell + System.Drawing 代码生成：蓝色渐变圆角 + 白色「OCR」，6 尺寸（16/32/48/64/128/256），手写 ICO 二进制（PNG 嵌入）。应用到 exe/任务栏/窗口/托盘。脚本 `gen-icon.ps1` 保留可调。

**踩坑**：PowerShell `DrawString` 把 `Rectangle` 当 `PointF`（重载解析），改用 `RectangleF` 明确类型。

---

## 六、功能保真清单（100% 保留）

| 功能 | v4 落点 |
|------|---------|
| 8 种 OCR 模式 + 结构化解析（银行卡/身份证/抗漂移排版）| BaiduOcrEngine + OcrModes |
| 智能中英方向 + 混合分段翻译 | BaiduTranslator + LanguageDetector |
| 4 分类 44 音色 TTS（HTTP+WS 双协议）| VoiceCatalog + BaiduSpeechSynthesizer |
| 文本切分排队播放 + 流释放 | MainWindow + MediaPlayer |
| 快捷翻译原地替换（Ctrl+C→译→Ctrl+V）| ClipboardService + QuickTranslate |
| Shift+S/F 系统热键 + 子类化 | HotkeyService |
| 托盘 / 开机自启 / 快捷键录制 | MainWindow / StartupService / SettingsWindow |
| 纯 Win32 截图选区（零闪烁/8 控制点）| Platform/CaptureWindow（原样保留）|
| OCR/翻译耗时统计 / 双栏滚动同步 / 字体切换 / 窗口位置记忆 / 强制前置 | 全部保留 |

---

## 七、最终产物结构

```
OcrTranslator_v4.0/
├── 源码 ~30 文件（Abstractions/Models/Services/Platform/ViewModels/Views/Styles）
├── Assets/        app.ico（v4 新生成）+ 6 尺寸 PNG + Lottie/GIF
├── gen-icon.ps1   图标生成脚本（可调）
├── build.cmd      双击编译（vswhere 自动定位 VS MSBuild）
└── bin/x64/Debug/.../OcrTranslator.exe  （181KB，框架依赖）
```

---

## 八、编译与运行

**编译**（必须 VS MSBuild，不要 dotnet build）：
```bash
# 方式1：双击 build.cmd
# 方式2：命令行
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" ^
  OcrTranslator.csproj -p:Configuration=Debug -p:Platform=x64
```

**运行**：`bin\x64\Debug\net10.0-windows10.0.19041.0\OcrTranslator.exe`

**部署模式**：框架依赖（`SelfContained=false` + `WindowsAppSDKSelfContained=false`），依赖本机 .NET 10 Desktop Runtime + Windows App SDK 1.7 Runtime，不附带运行时（与 v3 一致）。

**编译时 exe 被占用**：直接 `powershell.exe -Command "Stop-Process -Name OcrTranslator -Force"` 关闭运行实例再编译（已授权，不用 taskkill 因 bash 转义 /F）。

---

## 九、与 v3 的完全隔离

| 项 | v3 | v4 |
|----|----|-----|
| 配置 | `%LOCALAPPDATA%\GeekOCR` | `%LOCALAPPDATA%\GeekOCR_v4` |
| 日志 | `GeekOCR\logs` | `GeekOCR_v4\logs` |
| 开机自启注册表键 | `GeekOCR` | `GeekOCR_v4` |
| TTS cuid | `geek_ocr` | `geek_ocr_v4` |

v4 首次启动为空配置，需重新填 API 密钥。两者可共存。

**安全修复**：清除了 v3 `BaiduApi.cs` 硬编码的翻译 AppID/SecretKey，全部走配置。

---

## 十、经验总结（供复用）

1. **WinUI 3 编译必须用 VS MSBuild**，`dotnet build` 的 XamlCompiler 遇 XAML 错误静默崩溃，不报告。这是本项目最耗时的一坑。
2. **`dotnet build` 静默崩（MSB3073 无输出）= XAML 语义错误被吞**，换 VS MSBuild 立见真错。
3. **配置存储的 Set 要批量**：循环里调 Set 会频繁写盘卡顿，用计数式 BeginBatch/EndBatch 兜底。
4. **提示消息不要覆盖业务内容**：用 InfoBar 非侵入横幅，而非塞进文本框。
5. **v3 → v4 重构**：框架已最优时，升级=兑现框架现代能力（Mica/主题/token/DI/MVVM/接口），而非换框架。
6. **未打包 WinUI 3 部署**：`WindowsPackageType=None` + `WindowsAppSDKSelfContained=false` + 显式 Bootstrap + JSON 配置（替代 ApplicationData.Current）。
7. **PowerShell DrawString**：用 `RectangleF` 而非 `Rectangle`（避免重载解析当 PointF）。
8. **Git Bash 的 taskkill**：`/F` 被转义成路径，改用 PowerShell `Stop-Process`。

---

*文档生成时间：2026-06-23 · 极客 OCR v4.0*
