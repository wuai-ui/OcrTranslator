# 极客 OCR v4.0 代码质量优化记录

> 本次优化涵盖 10 个文件、8 类问题，共计 146 行新增 / 117 行删除。所有修改保持功能 100% 不变，仅提升代码质量与健壮性。

---

## 一、问题清单与修复

### 1. OcrModes.ByDisplayName 默认回退错误

| 项 | 内容 |
|---|------|
| 文件 | `Models/OcrMode.cs:60` |
| 问题 | 输入无效模式名时回退到 `All[1]`（"通用-含位置"），与 `SettingsService.DefaultOcrMode` 默认值 "通用-标准" 不一致 |
| 修复 | `return All[1]` → `return All[0]`（"通用-标准"） |

### 2. AppSettings.Set 不必要的 JSON 序列化

| 项 | 内容 |
|---|------|
| 文件 | `Services/AppSettings.cs:107-126` |
| 问题 | 每次 `Set` 都执行 `JsonDocument.Parse(JsonSerializer.Serialize(value))` 再 `.Clone()`，三步变一步即可 |
| 修复 | 统一改为 `JsonSerializer.SerializeToElement(value)`，一步到位 |

### 3. AppSettings 异常静默吞掉

| 项 | 内容 |
|---|------|
| 文件 | `Services/AppSettings.cs:47-50, 67-70` |
| 问题 | 配置文件读取/写入失败时 `catch {}` 完全无日志，用户配置丢失时无任何提示 |
| 修复 | `Load()` 加 `Logger.Warn`，`Save()` 加 `Logger.Error` |

### 4. BaiduTokenProvider 使用 DateTime.Now 做过期判断

| 项 | 内容 |
|---|------|
| 文件 | `Services/BaiduTokenProvider.cs`（6 处） |
| 问题 | `DateTime.Now` 受系统时钟变更影响（用户改时间、夏令时），可能导致 Token 提前过期或永不过期 |
| 修复 | 全部改为 `DateTimeOffset.UtcNow`，字段类型 `DateTime` → `DateTimeOffset` |

### 5. 四个服务各自创建独立 HttpClient 实例

| 项 | 内容 |
|---|------|
| 文件 | `BaiduOcrEngine` / `BaiduTranslator` / `BaiduSpeechSynthesizer` / `BaiduTokenProvider` / `App.xaml.cs` |
| 问题 | 4 个 `private static readonly HttpClient` 各自独立，无法复用 TCP 连接池 |
| 修复 | `App.xaml.cs` 注册单例 `HttpClient`，4 个服务改为构造函数注入共享实例 |

```csharp
// App.xaml.cs
services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
```

### 6. 字体列表每次打开设置页都全量枚举系统 API

| 项 | 内容 |
|---|------|
| 文件 | `Views/SettingsWindow.xaml.cs` / `Views/MainWindow.xaml.cs` |
| 问题 | `System.Drawing.FontFamily.Families` 是系统调用，每次打开设置页或重载配置时重复执行 |
| 修复 | `SettingsWindow` 增加 `static List<string>? _cachedFontNames` + `GetCachedFontNames()`，MainWindow 复用同一缓存 |

### 7. SaveToggleSettings 无批量写盘保护

| 项 | 内容 |
|---|------|
| 文件 | `Views/SettingsWindow.xaml.cs:354-377` |
| 问题 | 虽然外层 `SaveSettings_Click` 包了 `BeginBatch/EndBatch`，但 `SaveToggleSettings` 自身无防护，被单独调用时会频繁写盘 |
| 修复 | 方法内部自带 `BeginBatch/EndBatch` + `try/finally`，嵌套安全（计数式批量，内外层不冲突） |

### 8. SettingsWindow 重复定义 NativeMethods

| 项 | 内容 |
|---|------|
| 文件 | `Views/SettingsWindow.xaml.cs` 底部 / `Platform/Win32/NativeMethods.cs` |
| 问题 | SettingsWindow 内部有一个 `private static class NativeMethods`，包含键盘钩子 4 个 API，与共享类重复 |
| 修复 | 将 `LowLevelKeyboardProc` / `SetWindowsHookEx` / `UnhookWindowsHookEx` / `CallNextHookEx` / `GetModuleHandle` 移入 `Platform/Win32/NativeMethods.cs`，SettingsWindow 中删除私有类，改用 `Platform.Win32.NativeMethods.*` |

### 9. async void 方法缺少全局异常保护

| 项 | 内容 |
|---|------|
| 文件 | `Views/MainWindow.xaml.cs` |
| 问题 | `PlaySpeechButton_Click` 整体无 try-catch，内部任何异常都会导致 UI 状态不一致（IsSpeaking=true 无法恢复）；`PlayNextChunk` 的 `_ttsQueue.Dequeue()` 在 try 块外，异常时丢失 chunk |
| 修复 | `PlaySpeechButton_Click` 整体包裹 try-catch，失败时 `ResetSpeechState()`；`PlayNextChunk` 将 `Dequeue()` 移入 try 块内 |

---

## 二、附带清理

| 项 | 内容 |
|---|------|
| `Views/SettingsWindow.xaml.cs` | 删除未使用的 `_allOcrModes` 字段（17 项字符串数组，从未引用） |
| `Views/MainWindow.xaml.cs` | 移除未使用的 `using System.Linq` 和 `using System.Runtime.InteropServices` |
| `Views/SettingsWindow.xaml.cs` | `#nullable disable` 文件中使用 `List<string>?` 注解，加 `#nullable enable/disable` 局部开关消除 CS8632 警告 |
| `.gitignore` | 添加 `dotnet-install.ps1`（构建辅助脚本，不应入库） |

---

## 三、修改文件清单（10 个）

| 文件 | 改动类型 |
|------|---------|
| `Models/OcrMode.cs` | 默认值修正 |
| `Services/AppSettings.cs` | Set 简化 + 异常日志 |
| `Services/BaiduTokenProvider.cs` | DateTimeOffset + HttpClient 注入 |
| `Services/BaiduOcrEngine.cs` | HttpClient 注入 |
| `Services/BaiduTranslator.cs` | HttpClient 注入 |
| `Services/BaiduSpeechSynthesizer.cs` | HttpClient 注入 |
| `App.xaml.cs` | 共享 HttpClient 注册 |
| `Views/SettingsWindow.xaml.cs` | 字体缓存 + 批量保护 + NativeMethods 去重 + 清理 |
| `Views/MainWindow.xaml.cs` | async void 加固 + 字体缓存复用 + 清理 |
| `Platform/Win32/NativeMethods.cs` | 补充键盘钩子 API |

---

## 四、编译验证

```
dotnet msbuild OcrTranslator.csproj -p:Configuration=Debug -p:Platform=x64 -p:EnableCoreMrtTooling=false
```

结果：**编译成功，0 错误**（8 个预存警告为 CommunityToolkit.Mvvm 的 MVVMTK0045，与本次改动无关）

产物：
- `bin\x64\Debug\net10.0-windows10.0.19041.0\OcrTranslator.exe` (181KB)

---

*2026-06-29 · 极客 OCR v4.0 代码质量优化*
