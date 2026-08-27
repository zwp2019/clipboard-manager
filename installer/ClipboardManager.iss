; Inno Setup 6 打包脚本 —— 剪贴板管理器
; 使用方法：
;   1. 先发布程序：
;      dotnet publish src\ClipboardManager.App\ClipboardManager.App.csproj -c Release -r win-x64 --self-contained true
;   2. 用 Inno Setup Compiler（ISCC.exe）编译本脚本：
;      ISCC.exe installer\ClipboardManager.iss
;   3. 生成安装包：installer\Output\剪贴板管理器Setup-1.0.0.exe

#define MyAppName "剪贴板管理器"
#define MyAppNameEn "ClipboardManager"
#define MyAppVersion "1.0.0"
#define MyAppExeName "ClipboardManager.App.exe"
#define MyAppPublisher "ClipboardManager"
#define PublishDir "..\src\ClipboardManager.App\bin\Release\net10.0-windows10.0.19041.0\win-x64"

[Setup]
; 固定的合法 GUID：覆盖安装/升级的唯一身份标识，发布后绝不可再改动
AppId={{7A1F4E2C-8B3D-4C6A-9E5F-1A2B3C4D5E6F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppNameEn}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; 输出目录与安装包文件名
OutputDir=Output
OutputBaseFilename=剪贴板管理器Setup-{#MyAppVersion}
; 压缩与固态安装（体积更小、安装更快）
Compression=lzma2/max
SolidCompression=yes
; 仅 x64。按当前用户安装（无需管理员权限，无 UAC 弹窗）：
; 程序装到 %LOCALAPPDATA%\Programs，自启/数据等 per-user 区域指向正确的用户目录
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
; 卸载时显示"是否同时删除用户数据"由 [Code] 控制
UninstallDisplayIcon={app}\{#MyAppExeName}
; 覆盖安装/升级时自动关闭正在运行的程序，无需手动结束进程
CloseApplications=yes
RestartApplications=no
; 安装包界面语言（简体中文；若提示缺 isl，请把中文语言文件放入 Inno 的 Languages 目录）
ShowLanguageDialog=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加图标:"; Flags: checkedonce
Name: "autostart"; Description: "开机自动启动(&S)"; GroupDescription: "其他:"

[Files]
; 整个自包含发布目录全部打入安装包
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载{#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
; 开机自启：写入当前用户 Run 注册表
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "运行{#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 安装包卸载前先结束正在运行的程序
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
; 卸载后清理日志目录（数据库保留在 LOCALAPPDATA，不随卸载删除）
Type: filesandordirs; Name: "{localappdata}\ClipboardManager\logs"

[Messages]
SelectDirDesc=安装位置
SelectDirLabel3=安装程序将把 [name] 安装到以下文件夹。
FinishedLabelNoIcons=安装完成。%n%n首次运行时会自动创建剪贴板历史数据库。
FinishedLabel=安装完成。%n%n首次运行时会自动创建剪贴板历史数据库。
