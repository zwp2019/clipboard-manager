# Clipboard Manager · 剪贴板管理器

> Never lose a copied snippet again. A native clipboard history tool built for Windows.

<p align="center">
  <a href="README.md">中文</a> · <strong>🌐 English</strong>
</p>

<p align="center">
  <strong>Fast · Native · Private · Practical</strong><br>
  Windows 10 1809+ / Windows 11 · x64 / ARM64 · Apache-2.0
</p>

Press <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>V</kbd> to search anything you recently copied — text, images, links, or files. Press Enter to copy it and hide the panel, then paste into your target input. All history stays in a local SQLite database, and full-text search works across Chinese, English, code, and URLs.

<p align="center">
  <img src="截图/截图1.png" alt="Clipboard history view showing image cards, file records, and the favorites entry" width="100%">
</p>

<p align="center"><sub>Browse images and files, open details, and pin the snippets you use often.</sub></p>

## Why Clipboard Manager

- **Recover anything you copied**: Records text, images, URLs, HTML, RTF, and files. Filter by keyword, type, source app, or date.
- **Chinese-friendly search**: Built on SQLite FTS5, with tuning for Chinese word segmentation and short-term matching.
- **Faster copy-paste**: Open it with a global hotkey, select with the keyboard, and copy. Supports plain-text copy, bulk actions, and a sequential copy queue.
- **Keep common snippets handy**: Organize them into favorite boards, edit text, add frequently used phrases, or drag items onto a board.
- **Search inside images**: Extracts text from images with the built-in Windows OCR and includes it in search. Previews and thumbnails are cached locally.
- **Private by default**: Data stays on your machine. Exclude specific apps, pause recording, and back up or restore your data.
- **A true Windows app**: Built with WinUI 3, with light and dark themes, a system tray icon, multi-monitor, and high-DPI support.

## Getting Started

### Download and install

Get the latest Windows installer from [GitHub Releases](https://github.com/zwp2019/clipboard-manager/releases) or [Gitee Releases](https://gitee.com/kyrie_zwp/clipboard-manager/releases). After installing, launch the app and press `Ctrl+Shift+V` to open your clipboard history.

### Build from source

Requirements: Windows 10 1809+ or Windows 11, and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). To build the WinUI 3 app, use the Developer PowerShell for Visual Studio 2026:

```powershell
msbuild ClipboardManager.sln /restore /p:Configuration=Release
```

The business and test projects can also be built and run with the .NET CLI:

```powershell
dotnet build src/ClipboardManager.Application -c Release
dotnet build src/ClipboardManager.Data -c Release
dotnet test tests/ClipboardManager.Tests -c Release
```

> WinUI 3 depends on the Windows packaging toolchain, so this project cannot be built or run on macOS or Linux.

## Keyboard Shortcuts

| Action | Description |
|---|---|
| `Ctrl+Shift+V` | Open clipboard history (configurable in Settings) |
| `←` / `→`, `Enter` | Select an item, copy it, and hide the panel |
| `Ctrl+C` | Copy the current card and hide the panel |
| `Space` | Preview text or image details |
| `Shift+Enter` | Copy as plain text and hide the panel |
| `Esc` | Close the window |

> **Hotkey conflict:** Some Chinese input methods use `Ctrl+Shift+V` to switch between Simplified and Traditional Chinese, which can conflict with the default hotkey. If that happens, change the global hotkey in Settings.

You can also multi-select, bulk favorite, group, or delete items, drag and drop, filter quickly, and add several items to a copy queue for the current session. See the [local experience guide](docs/LOCAL_PASTE_EXPERIENCE.md) for details.

## Tech Stack

C# · .NET 10 · WinUI 3 · Windows App SDK · SQLite / FTS5 · Dapper · CommunityToolkit.Mvvm

The project uses a layered structure: Domain, Contracts, Application, Data, Infrastructure, and App. Automated tests live in `tests/ClipboardManager.Tests`.

## Known Limitations

- Selecting a history item only writes it to the system clipboard. It is not pasted into other apps automatically; press `Ctrl+V` in the target input.
- Image OCR relies on the Windows OCR language packs installed on your system, so recognition quality depends on them.
- Quit the app before restoring a database, so the database file is not locked.

## Contributing

Issues, feature requests, and pull requests are welcome. Before you start, please read the [architecture notes](docs/Architecture.md) and the [development spec](AI_DEVELOPMENT_SPEC.md).

If you find this project useful, please give it a **Star ⭐** so more people can discover it.

## License

Released under the [Apache-2.0](LICENSE) license.
