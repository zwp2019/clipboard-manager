# AI_DEVELOPMENT_SPEC.md

# Windows Clipboard Manager

# AI Development Specification

> 本文件是整个项目的最高级 AI 开发规范。
>
> 所有 AI Coding Agent 在修改、创建、删除任何代码之前，都必须先阅读本文件。
>
> 本文件优先级高于普通开发任务描述。
>
> 如果用户提出的任务与本文件冲突，必须先指出冲突，不允许擅自违反本规范。

---

# 1. 项目概述

项目名称：

Windows Clipboard Manager

项目目标：

开发一款专业的 Windows 10 / Windows 11 原生剪贴板管理软件。

产品定位：

* 类似 Paste 的剪贴板管理体验
* 面向 Windows 用户
* 原生 Windows Desktop
* 极快启动
* 极低后台资源占用
* 强大的剪贴板历史
* 快速搜索
* 键盘优先
* 图片预览
* 快速粘贴
* 系统托盘
* 全局快捷键
* 收藏
* 标签
* 历史管理
* 隐私保护
* 后续可扩展 AI 功能

最终目标：

不是 Demo。

不是技术验证。

不是一次性脚本。

目标是：

Production-ready Windows Desktop Application。

---

# 2. 核心产品原则

产品必须遵循：

## 2.1 Native First

优先使用 Windows 原生能力。

必须优先考虑：

* WinUI 3
* Windows App SDK
* Win32
* WinRT
* Windows Clipboard API
* Windows Shell API
* Windows Registry
* Windows Startup
* Windows Global Hotkey

禁止为了开发方便而引入 Web Runtime。

---

# 3. 技术栈

必须使用：

* C#
* .NET 10
* WinUI 3
* Windows App SDK
* XAML
* SQLite
* SQLite FTS5
* MVVM
* Dependency Injection
* Async/Await
* Win32 / WinRT

推荐：

* CommunityToolkit.Mvvm
* Microsoft.Extensions.DependencyInjection
* Microsoft.Extensions.Logging
* Serilog
* SQLite
* Dapper 或 EF Core

如果需要选择：

数据库优先：

SQLite + Dapper

原因：

剪贴板软件数据库操作主要是：

* 插入
* 查询
* 搜索
* 删除
* 更新

不需要非常复杂的 ORM。

---

# 4. 明确禁止

禁止使用：

* Electron
* React
* Vue
* Angular
* Flutter
* Tauri
* WebView 作为主 UI
* Java
* Spring Boot
* Node.js 作为核心运行时

允许：

* Node.js 作为开发工具
* Python 作为辅助脚本
* Web 工具用于生成资源

但最终运行程序不能依赖这些 Runtime。

---

# 5. Windows 支持范围

最低：

Windows 10 1809

主要支持：

Windows 10
Windows 11

架构：

* x64
* ARM64

必须考虑：

* 高 DPI
* 多显示器
* Windows 缩放比例
* 深色模式
* 浅色模式
* Mica
* Acrylic
* RDP
* UAC
* 管理员程序
* 系统休眠
* 系统恢复
* 用户快速切换

---

# 6. UI 产品定位

视觉方向：

Modern
Minimal
Native
Elegant
Fast

参考优秀剪贴板软件的：

* 信息架构
* 搜索方式
* 键盘操作
* 快速唤起
* 内容预览
* 快速粘贴

但是：

禁止：

* 复制第三方 Logo
* 复制第三方品牌
* 复制第三方图标
* 复制第三方图片
* 复制第三方源码
* 直接 1:1 复制第三方受版权保护的视觉资源

可以借鉴：

UX Pattern
Interaction Pattern
Information Architecture

最终形成自己的 Windows Design System。

---

# 7. 核心功能

第一阶段必须实现：

* Clipboard Monitor
* Text
* Image
* URL
* HTML
* RTF
* File
* History
* Search
* Favorite
* Global Hotkey
* Quick Paste
* System Tray
* Auto Start
* Dark Mode
* Light Mode
* Settings

第二阶段：

* Tags
* Smart Categories
* Sensitive Content Detection
* History Expiration
* Image compression
* Thumbnail cache
* Import / Export
* Backup
* Restore

第三阶段：

* AI 分类
* AI 搜索
* AI 摘要
* AI OCR
* Semantic Search
* Embedding
* Local AI

---

# 8. 架构

采用：

Clean Architecture + MVVM + Dependency Injection

推荐：

Presentation
Application
Domain
Infrastructure
Data

结构：

```text
ClipboardManager
│
├── ClipboardManager.App
│
├── ClipboardManager.Application
│
├── ClipboardManager.Domain
│
├── ClipboardManager.Infrastructure
│
├── ClipboardManager.Data
│
├── ClipboardManager.Contracts
│
└── ClipboardManager.Tests
```

---

# 9. Domain

Domain 不允许依赖：

* WinUI
* Windows UI
* SQLite
* Dapper
* EF Core
* Win32

Domain 只负责业务模型。

核心：

ClipboardItem

ClipboardType

Tag

Favorite

Settings

---

# 10. ClipboardItem

ClipboardItem 至少包含：

```text
Id
Type
Text
Html
Rtf
ImagePath
ThumbnailPath
FilePaths
SourceApplication
SourceTitle
CreatedAt
UpdatedAt
Hash
Size
IsFavorite
IsDeleted
```

Type：

```text
Text
Image
Html
Rtf
Url
File
Code
Unknown
```

---

# 11. Clipboard Engine

核心模块：

```text
ClipboardMonitor
ClipboardReader
ClipboardNormalizer
ClipboardHashService
ClipboardDeduplicationService
ClipboardStorageService
```

流程：

```text
Windows Clipboard Changed
        ↓
ClipboardMonitor
        ↓
ClipboardReader
        ↓
读取格式
        ↓
ClipboardNormalizer
        ↓
统一 ClipboardItem
        ↓
Hash
        ↓
Deduplication
        ↓
Database
        ↓
Thumbnail / Image Processing
        ↓
Search Index
        ↓
UI
```

---

# 12. Clipboard Listener

禁止：

```text
while(true)
{
    Thread.Sleep(...)
    CheckClipboard()
}
```

禁止轮询。

优先使用 Windows 原生事件机制。

必须处理：

* 高频复制
* 重复事件
* Clipboard Lock
* Clipboard 读取失败
* 其他程序正在使用 Clipboard
* 自己写 Clipboard 后产生的事件

必须具备：

Retry
CancellationToken
Exception Handling
Logging

---

# 13. 防止死循环

当程序自己执行：

```text
Clipboard.SetData(...)
```

不能再次把自己写入的内容当作新的历史。

必须维护：

* Internal Clipboard Operation Flag
* Hash
* Operation ID

避免：

```text
Copy
↓
ClipboardChanged
↓
Save
↓
SetClipboard
↓
ClipboardChanged
↓
Save
↓
无限循环
```

---

# 14. Hash

每一个 ClipboardItem 必须计算 Hash。

文本：

Hash(Text)

图片：

Hash(Image Content)

文件：

根据文件路径和必要的 Metadata 生成 Hash。

推荐：

SHA-256

如果性能测试证明 SHA-256 成为瓶颈，可以考虑：

XXHash

但是不要过早优化。

---

# 15. 去重

如果用户连续复制相同内容：

```text
Hello
Hello
Hello
Hello
```

默认不要生成四条历史。

应该：

```text
更新 created_at
```

使其移动到最新位置。

用户可以在 Settings 中选择：

* 去重
* 不去重

---

# 16. SQLite

使用：

SQLite

启用：

WAL

合理配置：

* busy_timeout
* foreign_keys
* journal_mode
* synchronous

数据库必须支持：

* Migration
* Backup
* Restore
* Integrity Check

---

# 17. SQLite 表

至少：

```text
clipboard_items
clipboard_search
tags
clipboard_tags
settings
```

---

# 18. FTS5

全文搜索使用：

SQLite FTS5

搜索必须支持：

* 中文
* 英文
* 数字
* URL
* SQL
* Java
* C#
* JavaScript
* JSON
* Markdown

搜索不能：

```text
SELECT *
```

然后全部加载到 C# 再搜索。

必须尽可能由数据库完成搜索。

---

# 19. 数据库性能

必须测试：

10,000 条

100,000 条

500,000 条

ClipboardItem。

必须检查：

* Insert
* Search
* Delete
* Favorite
* Pagination

不能假设只有几百条数据。

---

# 20. 图片存储

禁止默认把所有原始图片直接塞入 SQLite。

推荐：

```text
SQLite
│
├── metadata
│
└── image_path
```

文件系统：

```text
AppData
│
├── Database
│
├── Images
│
├── Thumbnails
│
├── Cache
│
├── Logs
│
└── Backups
```

---

# 21. 图片处理

必须：

异步处理。

禁止 UI Thread 处理大图片。

图片流程：

```text
Clipboard Image
        ↓
Temporary Stream
        ↓
Save Original
        ↓
Generate Thumbnail
        ↓
Cache
        ↓
Database
```

必须处理：

* 大图片
* 超大图片
* PNG
* JPEG
* Bitmap
* DIB
* 透明背景

必须释放：

Stream
Bitmap
Image Resource

---

# 22. Thumbnail

列表只加载 Thumbnail。

用户打开详情时：

再加载 Original。

禁止：

列表直接加载几十 MB 原图。

Thumbnail 必须：

* 缓存
* Lazy Load
* 异步
* 可取消

---

# 23. UI 架构

使用：

MVVM

禁止：

```text
MainWindow.xaml.cs
```

写大量业务代码。

禁止：

View 直接访问：

Database
Repository

必须：

```text
View
 ↓
ViewModel
 ↓
Application Service
 ↓
Repository
 ↓
SQLite
```

---

# 24. UI 设计

主 Launcher：

```text
┌──────────────────────────────┐
│ 🔍 Search clipboard...       │
├──────────────────────────────┤
│                              │
│ Today                        │
│                              │
│ ┌──────────────────────────┐ │
│ │ Clipboard Item           │ │
│ └──────────────────────────┘ │
│                              │
│ ┌──────────────────────────┐ │
│ │ Clipboard Item           │ │
│ └──────────────────────────┘ │
│                              │
├──────────────────────────────┤
│ Favorites   History Settings │
└──────────────────────────────┘
```

---

# 25. UI 风格

使用：

* WinUI 3
* Fluent
* Mica
* Acrylic
* Rounded Corners
* Shadow
* Subtle Animation
* High DPI

但是：

不要过度 Blur。

不要大量使用：

* DropShadow
* Blur
* Gradient
* Shader
* 复杂动画

性能优先。

---

# 26. UI Virtualization

绝对禁止：

一次性创建：

10,000 个 ClipboardItem UI。

必须使用：

Virtualization
Lazy Loading
Pagination

目标：

即使：

100,000 条历史

UI 也必须保持流畅。

---

# 27. Search

搜索框：

用户输入：

```text
rabbit
```

实时显示：

```text
RabbitMQ
RabbitMQ consumer
RabbitMQ configuration
```

搜索必须：

* Async
* Debounce
* CancellationToken

例如：

用户连续输入：

```text
r
ra
rab
rabb
rabbi
rabbit
```

不能执行六次完整搜索而全部同时返回。

旧搜索必须取消。

---

# 28. Global Hotkey

默认：

Ctrl + Shift + V

必须支持：

* 自定义
* 冲突检测
* 注册失败
* 恢复默认

快捷键：

```text
Ctrl + Shift + V
```

唤起 Launcher。

---

# 29. Launcher 行为

快捷键：

```text
Ctrl + Shift + V
```

↓

显示 Launcher。

默认：

搜索框获得焦点。

用户：

```text
↑
↓
```

选择。

```text
Enter
```

粘贴。

```text
Escape
```

关闭。

---

# 30. Quick Paste

流程：

```text
User selects item
        ↓
Write item to Clipboard
        ↓
Activate previous application
        ↓
Send Paste
        ↓
Restore Clipboard if required
        ↓
Hide Launcher
```

必须考虑：

* 当前活动窗口
* 当前活动进程
* 管理员程序
* UAC
* RDP
* Clipboard Lock

---

# 31. System Tray

应用后台运行时：

系统托盘显示图标。

菜单：

```text
Open Clipboard
Settings
Pause Monitoring
Clear History
Quit
```

---

# 32. Auto Start

支持：

Windows 登录后自动启动。

用户可以：

Enable
Disable

不能：

未经用户允许修改启动项。

---

# 33. Startup

启动目标：

用户体感必须非常快。

启动阶段只做：

```text
Initialize
Register Hotkey
Start Clipboard Monitor
Create Window
Show UI
```

不要在启动阶段：

* 加载全部历史
* 加载全部图片
* 执行全库搜索
* Vacuum
* Backup
* Scan Files
* 生成全部 Thumbnail

---

# 34. 启动优化

使用：

Lazy Initialization

Async Initialization

Cache

目标：

Cold Start

Warm Start

都需要测试。

不要为了追求一个绝对毫秒数字而牺牲稳定性。

---

# 35. 进程架构

默认优先：

Single Process

除非性能测试和稳定性测试证明必须拆分。

不要为了“看起来高级”而拆：

UI.exe
Core.exe

如果拆进程没有明确收益，就不要拆。

如果未来确实需要：

```text
ClipboardManager.exe
ClipboardManager.Core.exe
```

使用：

Named Pipe

通信。

---

# 36. Threading

UI Thread：

只处理 UI。

Background：

处理：

* Database
* Image
* Search
* File
* Hash
* Thumbnail

所有耗时操作：

Async

所有异步操作：

支持 CancellationToken。

---

# 37. Memory

后台常驻时：

目标：

尽可能低内存。

禁止：

缓存无限增长。

必须：

LRU Cache 或合理缓存策略。

图片缓存必须有上限。

搜索缓存必须有上限。

---

# 38. CPU

没有 ClipboardChanged 时：

不要持续消耗 CPU。

目标：

后台空闲时 CPU 接近 0%。

禁止：

高频 Timer。

禁止：

死循环轮询。

---

# 39. Sensitive Clipboard

剪贴板可能包含：

* Password
* Token
* Cookie
* API Key
* SSH Key
* Bank Information
* Private Message

必须设计：

Sensitive Clipboard Protection。

第一阶段可以提供：

用户排除应用。

例如：

```text
1Password
KeePass
Bitwarden
银行软件
密码管理器
```

用户可以添加：

Excluded Applications

---

# 40. History Expiration

设置：

保存：

1 天
7 天
30 天
90 天
永久

超过时间：

后台清理。

禁止在启动阶段进行大规模清理。

使用：

Background Task

---

# 41. Privacy

默认：

只在本机保存。

不上传剪贴板。

不联网。

除非用户主动开启：

Sync
AI
Cloud

任何网络功能：

必须明确提示用户。

---

# 42. Logging

使用：

Serilog 或 Microsoft.Extensions.Logging。

日志：

Info
Warning
Error

禁止日志记录：

* Password
* Token
* Cookie
* Clipboard 原文
* Sensitive Image

日志只记录：

Type
Hash
Size
Source Application
Error

---

# 43. Exception Handling

禁止：

```csharp
catch(Exception)
{
}
```

不能静默吞异常。

必须：

* Log
* Recover
* Notify if required

但是：

不能因为一个 Clipboard 读取失败导致整个程序退出。

---

# 44. Clipboard 锁定

Windows Clipboard 可能被其他应用锁定。

必须：

Retry。

例如：

```text
Retry 1
Retry 2
Retry 3
```

每次：

短暂延迟。

超过：

记录 Warning。

不能导致：

Application Crash。

---

# 45. Crash Recovery

如果：

UI 崩溃：

Core 能够继续运行。

如果：

Database 出错：

进入 Recovery。

如果：

Image 损坏：

跳过该图片。

如果：

Clipboard 出错：

继续监听。

任何单个 Item 出错：

不能影响整个 Clipboard Engine。

---

# 46. Settings

Settings 至少支持：

* Global Hotkey
* Auto Start
* Theme
* History Retention
* Duplicate Handling
* Excluded Applications
* Maximum History
* Image Compression
* Clear History
* Backup
* Restore

---

# 47. Theme

支持：

System
Light
Dark

跟随 Windows：

默认：

System

---

# 48. Accessibility

支持：

Keyboard Navigation

Focus

Screen Reader

High Contrast

不能把所有操作都绑定鼠标。

---

# 49. 多显示器

Launcher 应显示在：

当前鼠标所在显示器。

或者：

当前活动窗口所在显示器。

必须正确处理：

* DPI
* Display Bounds
* Work Area

不能简单使用：

Screen.PrimaryScreen

---

# 50. DPI

必须支持：

100%
125%
150%
175%
200%

不能：

硬编码：

1920x1080

不能：

大量写死 px。

WinUI 布局优先使用：

Auto
Star
Relative Size

---

# 51. 测试

必须建立：

Unit Tests

Integration Tests

Performance Tests

---

# 52. 必测场景

Clipboard：

* Text
* Image
* URL
* HTML
* RTF
* File

高频：

```text
连续复制 100 次
连续复制 1000 次
```

历史：

```text
10,000
100,000
500,000
```

---

# 53. 性能测试

至少测试：

Cold Start

Warm Start

Search

Scroll

Insert

Delete

Image Load

Thumbnail

Memory

CPU

---

# 54. Bug 修复规范

任何 Bug：

必须：

1. 复现
2. 搜索代码
3. 分析调用链
4. 找 Root Cause
5. 最小修改
6. 编译
7. 测试
8. 回归

禁止：

为了修一个 Bug：

直接重写整个模块。

---

# 55. AI Coding 规则

AI 每次接收到任务：

第一步：

读取：

AI_DEVELOPMENT_SPEC.md

第二步：

读取相关代码。

第三步：

理解：

Architecture
Dependencies
Call Chain

第四步：

提出修改方案。

第五步：

执行修改。

第六步：

Build。

第七步：

Test。

第八步：

检查 Git Diff。

---

# 56. AI 禁止行为

AI 禁止：

* 删除代码但没有理由
* 修改公共 API 而不说明
* 大规模重构
* 修改无关文件
* 添加无必要依赖
* 引入 Web Runtime
* 引入 Electron
* 引入 Node Runtime
* 使用轮询监听 Clipboard
* 阻塞 UI Thread
* 忽略异常
* 创建 God Class
* 创建 3000 行 MainWindow
* 创建 2000 行 Service
* 重复实现已有功能

---

# 57. 每次任务的标准流程

AI 必须：

```text
Read Spec
   ↓
Inspect Repository
   ↓
Understand Architecture
   ↓
Identify Related Code
   ↓
Plan
   ↓
Implement
   ↓
Build
   ↓
Test
   ↓
Review Diff
   ↓
Report
```

---

# 58. 修改前必须回答

在复杂任务中：

```text
What files will change?

Why?

What architecture layer?

What dependencies?

What risks?
```

简单任务可以直接执行。

---

# 59. 完成任务后的报告

AI 必须输出：

## Changed

修改了什么。

## Files

修改了哪些文件。

## Reason

为什么。

## Testing

运行了什么测试。

## Build

是否成功。

## Risk

还有什么风险。

## Next

下一步建议。

---

# 60. Git

推荐：

```text
main
develop
feature/*
fix/*
```

每个功能独立 Commit。

例如：

```text
feat: implement clipboard monitor

feat: add sqlite repository

feat: add global hotkey

feat: add clipboard search

fix: handle clipboard lock

perf: optimize thumbnail loading
```

---

# 61. 开发阶段

严格按照以下阶段：

## Phase 0

Architecture

## Phase 1

Project Initialization

## Phase 2

Domain Model

## Phase 3

Clipboard Engine

## Phase 4

SQLite

## Phase 5

Image Storage

## Phase 6

Search

## Phase 7

WinUI Launcher

## Phase 8

Global Hotkey

## Phase 9

Quick Paste

## Phase 10

Tray

## Phase 11

Settings

## Phase 12

Privacy

## Phase 13

Performance

## Phase 14

Testing

## Phase 15

Windows Compatibility

## Phase 16

Packaging

## Phase 17

Auto Update

## Phase 18

Production Audit

---

# 62. Phase 0

只做：

架构设计。

不要编写大量业务代码。

输出：

Architecture Document

---

# 63. Phase 1

创建：

Solution
Projects
Dependencies

确保：

Build Success

---

# 64. Phase 2

实现：

Domain Model

不接数据库。

不接 UI。

---

# 65. Phase 3

实现：

Clipboard Engine。

目标：

可靠获取：

Text
Image
URL
HTML
RTF
File

---

# 66. Phase 4

实现：

SQLite

FTS5

Repository

Migration

---

# 67. Phase 5

实现：

Image Storage

Thumbnail

Cache

---

# 68. Phase 6

实现：

Search

Debounce

Cancellation

FTS5

---

# 69. Phase 7

实现：

WinUI 3 Launcher。

---

# 70. Phase 8

实现：

Global Hotkey。

---

# 71. Phase 9

实现：

Quick Paste。

---

# 72. Phase 10

实现：

System Tray。

---

# 73. Phase 11

实现：

Settings。

---

# 74. Phase 12

实现：

Privacy。

---

# 75. Phase 13

性能审查。

目标：

* Fast Startup
* Low CPU
* Low Memory
* Smooth UI

---

# 76. Phase 14

完整测试。

---

# 77. Phase 15

测试：

Windows 10
Windows 11
x64
ARM64

---

# 78. Phase 16

创建：

Release

Installer

MSIX

根据需要支持：

Unpackaged

---

# 79. Phase 17

Auto Update。

必须：

* 安全
* 可回滚
* 校验
* 用户可控制

---

# 80. Phase 18

Production Audit。

必须输出：

P0
P1
P2
P3

---

# 81. 当前任务规则

每次 AI 启动开发时：

首先检查：

```text
AI_DEVELOPMENT_SPEC.md
```

然后检查：

```text
当前 Phase
```

如果没有明确 Phase：

先询问用户当前要进入哪个 Phase。

如果项目已经存在：

不要重新初始化。

先分析：

```text
Current Architecture
Current Phase
Completed Features
Known Bugs
```

---

# 82. 当前产品优先级

优先级：

1. 稳定性
2. 正确性
3. 性能
4. 用户体验
5. UI 美观
6. 扩展性
7. 代码简洁

不要为了：

UI 动画

牺牲：

稳定性。

不要为了：

极限启动速度

牺牲：

数据完整性。

---

# 83. 最终目标

最终产品必须让用户感觉：

```text
复制
 ↓
系统无感知

按快捷键
 ↓
瞬间出现

搜索
 ↓
立即找到

Enter
 ↓
立即粘贴

关闭
 ↓
立即消失

后台
 ↓
几乎感觉不到它存在
```

产品核心：

Fast
Native
Reliable
Private
Beautiful

---

# END

AI 必须遵循本文件。

如果无法满足某项要求：

不要假装完成。

必须明确指出：

* 为什么无法完成
* 当前限制
* 可行方案
* 风险

然后等待下一步指令。
