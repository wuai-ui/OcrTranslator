# 极客 OCR v3.0 - 合并版

## 版本说明

v3.0 是 v1.0 和 v2.0 的最佳实践合并版本，取两者精华：

### 来自 v1.0 的优势
- **多平台支持**：x86、x64、ARM64 三平台编译
- **完整 csproj 配置**：SelfContained、WindowsAppSDKSelfContained、XBF 保障
- **Program.cs 显式 Bootstrap**：Win32 MessageBox 错误提示，详细的启动诊断
- **4 分类语音系统**：基础、精品、臻品、大模型，共 40+ 音色
- **音频下载功能**：支持保存朗读音频为 MP3/WAV
- **音频流管理**：_previousAudioStream 及时释放，防止内存泄漏
- **ComboBox 状态持久化**：记住用户的选择状态

### 来自 v2.0 的优势
- **纯 Win32 截图窗口**：零闪烁分层窗口，支持 8 点拖拽调整、选区移动、压暗遮罩
- **RegisterHotKey 热键系统**：系统级热键注册，比低级键盘钩子更可靠
- **Logger 日志系统**：按日滚动、自动清理 30 天旧日志、多级日志（Info/Debug/Warn/Error）
- **类型安全 AppSettings**：GetString/GetBool/GetInt/Set 类型化 API
- **更优 UI 设计**：Border 容器、PlaceholderText、语义化配色
- **窗口位置记忆**：保存/恢复窗口位置和大小
- **ForceForeground 可靠置顶**：AttachThreadInput 确保窗口激活
- **BaiduApi 线程安全**：SemaphoreSlim 保护 Token 刷新，SplitByLanguage 混合语言翻译

## 技术栈

- .NET 10 + WinUI 3 + Windows App SDK 1.7
- 百度云 OCR / 翻译 / TTS API
- H.NotifyIcon.WinUI 系统托盘
- System.Drawing.Common 图像处理

## 发布

```bash
# x64 发布
dotnet publish -c Release -r win-x64 --self-contained true

# ARM64 发布
dotnet publish -c Release -r win-arm64 --self-contained true

# x86 发布
dotnet publish -c Release -r win-x86 --self-contained true
```
