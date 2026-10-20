# 剪贴板管理器 · Clipboard Manager

> 复制过的内容，随时找回来。一个为 Windows 打造的原生剪贴板历史工具。

<p align="center">
  <strong>快速 · 原生 · 私密 · 好用</strong><br>
  Windows 10 1809+ / Windows 11 · x64 / ARM64 · Apache-2.0
</p>

按下 <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>V</kbd>，搜索刚刚复制的文字、图片、链接或文件，按 Enter 复制并收起面板，再到目标输入框手动粘贴。历史记录保存在本机 SQLite 数据库中，全文搜索支持中文、英文、代码和 URL。

<p align="center">
  <img src="截图/截图1.png" alt="剪贴板管理器历史视图，展示图片卡片、文件记录和收藏入口" width="100%">
</p>

<p align="center"><sub>历史一目了然：浏览图片与文件、打开详情、将常用内容收藏起来。</sub></p>

**觉得它有用？[到 Gitee 仓库点个 Star ⭐](https://gitee.com/kyrie_zwp/clipboard-manager)**，让更多 Windows 用户发现这个项目；也欢迎提 Issue、分享建议或参与贡献。

## 为什么用它

- **找回任何复制内容**：记录文本、图片、网址、HTML、RTF 和文件；按关键词、类型、来源应用和日期筛选。
- **搜索中文也顺手**：SQLite FTS5 全文搜索，配合中文分词与短词匹配。
- **复制更快**：全局热键呼出，键盘选择并复制；支持纯文本复制、批量操作和顺序复制队列。
- **把常用内容收好**：创建收藏板、整理片段；编辑文本、添加常用文本，或拖动内容到收藏板。
- **图片也能搜**：本地 Windows OCR 提取图片文字并纳入搜索；图片预览和缩略图缓存在本机。
- **保护隐私**：数据默认留在本地，可排除指定应用、暂停记录，也可以备份和恢复。
- **原生 Windows 体验**：WinUI 3、深浅色主题、系统托盘、多显示器和高 DPI 支持。

## 快速开始

### 下载使用

前往 [Releases 页面](https://gitee.com/kyrie_zwp/clipboard-manager/releases) 下载最新 Windows 安装包。安装后启动应用，按 `Ctrl+Shift+V` 打开剪贴板历史。

### 从源码构建

需要 Windows 10 1809+ 或 Windows 11，以及 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。构建 WinUI 3 应用时，推荐使用 Visual Studio 2026 Developer PowerShell：

```powershell
msbuild ClipboardManager.sln /restore /p:Configuration=Release
```

构建完成后，可以在 Visual Studio 中启动应用。业务层和测试项目也可通过 .NET CLI 构建：

```powershell
dotnet build src/ClipboardManager.Application -c Release
dotnet build src/ClipboardManager.Data -c Release
dotnet test tests/ClipboardManager.Tests -c Release
```

> WinUI 3 依赖 Windows 打包任务，桌面应用不能在 macOS 或 Linux 上构建运行。

## 常用操作

| 操作 | 用途 |
|---|---|
| `Ctrl+Shift+V` | 呼出剪贴板历史（可在设置中修改热键） |
| `←` / `→`、`Enter` | 选择条目，复制并收起面板 |
| `Ctrl+C` | 复制当前卡片并收起面板 |
| `Space` | 查看文本或图片详情 |
| `Shift+Enter` | 复制为纯文本并收起面板 |
| `Esc` | 关闭窗口 |

> **热键提示：** 部分中文输入法也会用 `Ctrl+Shift+V` 切换简体/繁体，可能与本应用的默认呼出热键冲突。遇到冲突时，请在应用设置中修改全局热键。

也支持多选、批量收藏/分组/删除、拖放、快速筛选，以及把多个条目加入本次会话的复制队列。完整的交互说明见[本地体验说明](docs/LOCAL_PASTE_EXPERIENCE.md)。

## 技术栈

C# · .NET 10 · WinUI 3 · Windows App SDK · SQLite / FTS5 · Dapper · CommunityToolkit.Mvvm

项目采用分层结构：Domain、Contracts、Application、Data、Infrastructure 和 App。自动化测试位于 `tests/ClipboardManager.Tests`。

## 已知限制

- 选择历史内容后只写入系统剪贴板，不自动填入其他应用；请在目标输入框按 Ctrl+V 粘贴。
- 图片 OCR 使用本地 Windows OCR 能力，具体语言效果取决于系统已安装的 OCR 语言包。
- 恢复数据库前请先退出应用，避免数据库文件被占用。

## 参与项目

欢迎提交 Issue、功能建议和 Pull Request。开始前可阅读[架构说明](docs/Architecture.md)与[开发规范](AI_DEVELOPMENT_SPEC.md)。

如果你喜欢这个项目，请给它一个 **Star ⭐**，这会帮助项目触达更多人。
