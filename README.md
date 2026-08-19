# Clipboard Manager

生产级 Windows 剪贴板管理软件（Windows 10 1809+ / Windows 11，x64 / ARM64）。
遵循 [AI_DEVELOPMENT_SPEC.md](./AI_DEVELOPMENT_SPEC.md) 规范开发。

**Fast · Native · Reliable · Private · Beautiful**

## 功能

- 剪贴板监听（原生事件，非轮询）：Text / Image / URL / HTML / RTF / File
- 历史记录（SQLite WAL + FTS5 全文搜索，支持中文/英文/代码/URL）
- 全局热键唤起（默认 `Ctrl+Shift+V`，可自定义 + 冲突检测）
- 快速粘贴（写剪贴板 → 激活前窗口 → 自动 Ctrl+V）
- 系统托盘（常驻、暂停监控、清空历史、退出）
- 收藏、去重、历史保留策略、最大历史上限
- 图片缩略图 + LRU 缓存，列表虚拟化（10 万条历史流畅）
- 深色/浅色/系统主题（Mica）
- 隐私保护：排除应用列表（密码管理器/银行软件不入库）
- 开机自启（用户显式开启）、备份/恢复
- 多显示器（Launcher 定位到鼠标所在屏幕）、PerMonitorV2 高 DPI

## 技术栈

C# / .NET 10 / WinUI 3 / Windows App SDK / XAML / SQLite (FTS5) / Dapper / MVVM (CommunityToolkit.Mvvm) / Serilog

## 目录结构

```
src/
├── ClipboardManager.Domain         领域模型（ClipboardItem / Tag / Favorite / AppSettings）
├── ClipboardManager.Contracts      DTO + 端口接口
├── ClipboardManager.Application    应用服务（管道 / 搜索 / 历史 / 快速粘贴 / 设置 / 清理）
├── ClipboardManager.Data           SQLite + FTS5 + Migration + Repositories + 备份
├── ClipboardManager.Infrastructure Win32/WinRT（监听 / 读写 / 热键 / 托盘 / 图片 / 自启）
└── ClipboardManager.App            WinUI 3 UI（Launcher / Settings / DI 组装）
tests/
└── ClipboardManager.Tests          单元 + 集成 + 性能测试
```

## 环境要求

- Windows 10 1809 或 Windows 11（x64 / ARM64）
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 或 Visual Studio 2026（安装「.NET 桌面开发」工作负载）

## 构建

```powershell
# 还原 + 构建全部（默认 win-x64 自包含）
dotnet build ClipboardManager.sln -c Release

# 构建 ARM64（修改 App.csproj 中 RuntimeIdentifier 为 win-arm64 后）
dotnet build src/ClipboardManager.App/ClipboardManager.App.csproj -c Release

# 仅构建可跨平台验证的纯 .NET 层（Domain/Application/Data/Tests）
dotnet build src/ClipboardManager.Application -c Release
dotnet build src/ClipboardManager.Data -c Release
```

> 注：WinUI 3 只能在 Windows 上构建运行。App 与 Infrastructure 项目在 macOS/Linux 无法编译。

## 运行

```powershell
dotnet run --project src/ClipboardManager.App -c Debug
# 或直接运行
.\src\ClipboardManager.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\ClipboardManager.App.exe
```

- 按 `Ctrl+Shift+V` 唤起 Launcher
- `↑`/`↓` 选择，`Enter` 粘贴，`Esc` 关闭
- 图标驻留系统托盘

## 测试

```powershell
# 单元 + 集成测试
dotnet test tests/ClipboardManager.Tests -c Release

# 含性能测试（10k 数据量，约 10 秒）
dotnet test tests/ClipboardManager.Tests -c Release --filter "Category=Performance"
```

## 开发阶段状态（规范 §61）

| Phase | 内容 | 状态 |
|---|---|---|
| 0 | 架构设计 | ✅ docs/Architecture.md |
| 1 | 项目初始化 | ✅ 7 个工程，Solution 可还原 |
| 2 | Domain 模型 | ✅ |
| 3 | Clipboard Engine | ✅ 原生事件监听/读取/归一化/哈希/去重 |
| 4 | SQLite + FTS5 + Migration | ✅ |
| 5 | 图片存储 + 缩略图 + LRU 缓存 | ✅ |
| 6 | 搜索（Debounce + 取消 + FTS5） | ✅ |
| 7 | WinUI 3 Launcher | ✅ 虚拟化列表 + Mica |
| 8 | 全局热键 | ✅ |
| 9 | 快速粘贴 | ✅ |
| 10 | 系统托盘 | ✅ |
| 11 | 设置 | ✅ |
| 12 | 隐私（排除应用） | ✅ |
| 13 | 性能审查 | 🔄 需 Windows 环境压测 100k/500k |
| 14 | 测试 | 🔄 代码已就绪，需 Windows 上 `dotnet test` |
| 15 | Windows 兼容性 | ⏳ 需真机验证（Win10/11、ARM64、RDP、UAC） |
| 16 | 打包（MSIX/Installer） | ⏳ 未开始 |
| 17 | 自动更新 | ⏳ 未开始 |
| 18 | Production Audit | ⏳ 未开始 |

## 已知限制与风险

1. **本项目在 macOS 上编写，未在本机编译验证**。包版本（WindowsAppSDK 1.7.3、.NET 10.0 等）若还原失败，请在 Windows 上用 `dotnet add package` 更新到最新稳定版。
2. **管理员/UAC 窗口**：普通权限进程无法向管理员窗口注入 Ctrl+V（Windows UIPI 限制），快速粘贴对该场景失效，需以管理员运行。
3. **FTS5 中文分词**：采用 bigram 方案；搜索短词（<3 字符）自动降级 LIKE，长词走全文索引。
4. **恢复数据库**：需先退出应用再执行（数据库被占用时复制会失败）。
5. 未打包运行依赖 .NET 自包含（已启用），如遇启动失败请检查 Windows App SDK 依赖。

## 下一步建议

1. 在 Windows 机器上还原并编译，跑通 `dotnet test`。
2. 压测 100k / 500k 数据量（`docs/bench`），按结果调整 FTS 索引策略。
3. 补 Phase 16 打包（MSIX + 签名）。
4. 按规范 §55 的流程对每个改动走「编译 → 测试 → 检查 Diff」。
