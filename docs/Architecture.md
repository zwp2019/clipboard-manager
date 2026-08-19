# Clipboard Manager — 架构设计文档

> 对应 `AI_DEVELOPMENT_SPEC.md` §61 Phase 0（Architecture）。

## 1. 总体架构

Clean Architecture + MVVM + Dependency Injection（规范 §8）。

```
┌─────────────────────────────────────────────────────────┐
│                    Presentation (WinUI 3)               │
│  App / MainWindow / SettingsWindow / ViewModels         │
│  ── 依赖注入根、托盘、热键接线、MVVM                    │
├─────────────────────────────────────────────────────────┤
│                      Application                        │
│  ClipboardPipelineService / SearchService / History     │
│  QuickPasteService / SettingsService / Cleanup / Hash   │
│  Normalizer / StorageService / Deduplication            │
├───────────────────────────┬─────────────────────────────┤
│        Domain             │        Contracts            │
│  ClipboardItem / Tag /    │  DTO + 端口接口（依赖倒置）  │
│  Favorite / AppSettings   │                             │
├───────────────────────────┴─────────────────────────────┤
│                  Infrastructure (Win32/WinRT)           │
│  ClipboardMonitor / Reader / Writer / CoreHostWindow    │
│  GlobalHotkey / Tray / ImageStorage / AutoStart /       │
│  ProcessHelper / DibDecoder                             │
├─────────────────────────────────────────────────────────┤
│                       Data (SQLite)                     │
│  ConnectionFactory / Migration / FTS5 / Repositories /  │
│  Backup / ChineseTokenizer                              │
└─────────────────────────────────────────────────────────┘
```

依赖方向：**Presentation → Application → (Domain, Contracts)；Infrastructure/Data → Contracts + Domain**。Domain 不依赖任何 UI / 数据库 / Win32（规范 §9）。

## 2. 关键设计决策

| 决策 | 理由 |
|---|---|
| SQLite + Dapper（非 EF Core） | 剪贴板数据操作以 Insert / 查询 / 搜索 / 删除为主，无需复杂 ORM（规范 §4） |
| Win32 `AddClipboardFormatListener` + `WM_CLIPBOARDUPDATE` | 原生事件驱动，**禁止轮询**（规范 §12） |
| message-only window 独立线程 | 承载剪贴板/热键/托盘消息，与 UI 解耦，UI 崩溃不影响 Core（规范 §45） |
| `IInternalClipboardGuard` 防死循环 | 自身写入剪贴板产生的事件被丢弃（规范 §13） |
| FTS5 external content + 中文 bigram 分词 | 支持中英文/代码/URL 全文搜索；短查询自动降级 LIKE（规范 §18） |
| 图片文件系统存储 + 缩略图 + LRU 缓存 | 禁止图片塞入 SQLite；列表只加载缩略图（规范 §20-§22、§37） |
| 单进程 | 无明确收益不拆进程（规范 §35） |
| 未打包 + 自包含部署 | 免安装 Windows App SDK Runtime，双击即用（规范 §78 可后续补 MSIX） |

## 3. 剪贴板数据流

```
WM_CLIPBOARDUPDATE
  → ClipboardMonitor（过滤自身写入）
  → ClipboardReader（读取各格式 + 锁定重试）
  → ClipboardNormalizer → ClipboardHashService（SHA-256）
  → Deduplication（时间窗口）→ ClipboardStorageService
      ├─ 图片：ImageStorageService（原图 + 缩略图落盘）
      ├─ 去重：同 hash → Touch（移动到最前）
      └─ 入库 + FTS5 索引 + 历史上限 Trim
  → UI（Launcher 实时刷新）
```

## 4. 数据目录（规范 §20）

```
%LocalAppData%\ClipboardManager\
├── Database\clipboard.db     (SQLite, WAL)
├── Images\{yyyyMM}\          (原始图片)
├── Thumbnails\{yyyyMM}\      (256px 缩略图)
├── Cache\
├── Logs\                     (Serilog 滚动日志)
└── Backups\                  (VACUUM INTO 快照)
```

## 5. 数据库 Schema（规范 §17）

`clipboard_items`（条目）/ `clipboard_search` + `clipboard_search_fts`（FTS5 external content）/ `tags` / `clipboard_tags` / `settings` / `schema_version`（迁移）。删除/更新通过触发器联动 FTS 索引。

## 6. 线程模型（规范 §36）

- UI 线程：仅 UI（通过 `IDispatcher` 切回）。
- 后台：数据库、图片处理、哈希、搜索、剪贴板读取（`Task.Run` + CancellationToken）。
- 全部异步操作支持取消。

## 7. 启动流程（规范 §33）

```
单实例检查 → 日志 → DI → DB 迁移 → 设置加载 → 窗口创建 →
主题 → 托盘 → 热键注册 → 管道启动 → 历史清理(延迟 2min) → UI
```

不在启动阶段加载全部历史 / 生成缩略图 / Vacuum。

## 8. 隐私与安全（规范 §39-§42）

- 纯本机，无网络功能。
- 排除应用列表（密码管理器 / 银行软件）不入库。
- 日志只记录 Type / Hash / Size / 来源应用，**不记录剪贴板内容**。
- 开机自启仅在用户显式开启时写注册表。

## 9. 测试策略（规范 §51-§53）

- 单元测试：Hash / Normalizer / 分词 / 去重。
- 集成测试：仓储 CRUD / 分页 / 收藏 / FTS5 中英文搜索（临时 SQLite）。
- 性能测试：10k Insert / Search / Pagination（`Category=Performance`）。
- 目标数据量：10k / 100k / 500k（500k 需手动压测，见 README）。
