# 剪贴板管理器 · Clipboard Manager

> 复制过的内容，随时找回来。一个为 Windows 打造的原生剪贴板历史工具。

<p align="center">
  <strong>🌐 中文</strong> · <a href="README.en.md">English</a>
</p>

<p align="center">
  <strong>快速 · 原生 · 私密 · 好用</strong><br>
  Windows 10 1809+ / Windows 11 · x64 / ARM64 · Apache-2.0
</p>

按下 <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>V</kbd>，搜索刚刚复制过的文字、图片、链接或文件，按 Enter 复制并收起面板，再到目标输入框粘贴即可。所有历史记录保存在本机 SQLite 数据库中，全文搜索同时支持中文、英文、代码和 URL。

<p align="center">
  <img src="截图/截图1.png" alt="剪贴板管理器历史视图，展示图片卡片、文件记录和收藏入口" width="100%">
</p>

<p align="center"><sub>浏览图片与文件、查看详情，并把常用内容收藏起来。</sub></p>

## 为什么选择它

- **找回任何复制内容**：记录文本、图片、网址、HTML、RTF 和文件，可按关键词、类型、来源应用和日期筛选。
- **中文搜索更顺手**：基于 SQLite FTS5 全文检索，并针对中文分词和短词匹配做了优化。
- **复制更快**：全局热键呼出，键盘即可选择并复制；支持纯文本复制、批量操作和顺序复制队列。
- **常用内容随手可取**：创建收藏板整理片段，编辑文本或添加常用语，也可以直接拖入收藏板。
- **图片也能搜索**：使用 Windows 本地 OCR 提取图片中的文字并纳入搜索，预览图与缩略图缓存在本机。
- **隐私可控**：数据默认只保存在本地，可排除指定应用、暂停记录，并支持备份与恢复。
- **原生 Windows 体验**：基于 WinUI 3，支持深色/浅色主题、系统托盘、多显示器和高 DPI。

## 快速开始

### 下载安装

前往 [GitHub Releases](https://github.com/zwp2019/clipboard-manager/releases) 或 [Gitee Releases](https://gitee.com/kyrie_zwp/clipboard-manager/releases) 下载最新的 Windows 安装包，安装后启动应用，按 `Ctrl+Shift+V` 即可打开剪贴板历史。

### 从源码构建

环境要求：Windows 10 1809+ 或 Windows 11，以及 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。推荐在 Visual Studio 2026 开发人员 PowerShell 中构建 WinUI 3 应用：

```powershell
msbuild ClipboardManager.sln /restore /p:Configuration=Release
```

业务层与测试项目也可以通过 .NET CLI 构建和运行：

```powershell
dotnet build src/ClipboardManager.Application -c Release
dotnet build src/ClipboardManager.Data -c Release
dotnet test tests/ClipboardManager.Tests -c Release
```

> WinUI 3 依赖 Windows 打包工具链，本项目无法在 macOS 或 Linux 上构建或运行。

## 常用快捷键

| 操作 | 说明 |
|---|---|
| `Ctrl+Shift+V` | 呼出剪贴板历史（可在设置中修改） |
| `←` / `→`、`Enter` | 选择条目，复制并收起面板 |
| `Ctrl+C` | 复制当前卡片并收起面板 |
| `Space` | 查看文本或图片详情 |
| `Shift+Enter` | 以纯文本形式复制并收起面板 |
| `Esc` | 关闭窗口 |

> **热键冲突提示：** 部分中文输入法默认使用 `Ctrl+Shift+V` 切换简繁体，可能与本应用的呼出热键冲突。如遇冲突，请在应用设置中修改全局热键。

此外还支持多选、批量收藏/分组/删除、拖放、快速筛选，以及把多个条目加入本次会话的复制队列。完整交互说明见 [本地体验说明](docs/LOCAL_PASTE_EXPERIENCE.md)。

## 技术栈

C# · .NET 10 · WinUI 3 · Windows App SDK · SQLite / FTS5 · Dapper · CommunityToolkit.Mvvm

项目采用分层结构：Domain、Contracts、Application、Data、Infrastructure 与 App。自动化测试位于 `tests/ClipboardManager.Tests`。

## 已知限制

- 选中历史条目后仅写入系统剪贴板，不会自动粘贴到其他应用，请在目标输入框中按 `Ctrl+V`。
- 图片 OCR 依赖系统已安装的 Windows OCR 语言包，识别效果取决于语言包。
- 恢复数据库前请先退出应用，以免数据库文件被占用。

## 参与贡献

欢迎提交 Issue、功能建议和 Pull Request。开始之前，建议先阅读 [架构说明](docs/Architecture.md) 与 [开发规范](AI_DEVELOPMENT_SPEC.md)。

如果这个项目对你有帮助，欢迎点一个 **Star ⭐**，让更多人看到它。

## 许可证

本项目基于 [Apache-2.0](LICENSE) 许可证开源。
