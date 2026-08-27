# Windows Clipboard Manager UI Design Specification

## 1. 角色

你是一名资深 Desktop Product Designer + Windows UI Engineer。

你的任务不是制作普通 CRUD 管理后台。

你需要设计并实现一款：

Professional Windows Clipboard Manager

产品体验参考：

- Paste
- Raycast
- Alfred
- Arc
- Linear
- macOS Spotlight

重点学习这些产品的：

- 信息层级
- 极简设计
- 快速搜索
- 键盘交互
- 动画
- 信息密度
- 快速操作

禁止直接复制第三方：

- Logo
- Icon
- 图片
- 品牌元素
- 源代码
- 受版权保护的视觉资源

必须形成自己的 Design System。

---

# 2. 产品核心体验

用户打开软件的核心流程：

复制内容

↓

按：

Ctrl + Shift + V

↓

Clipboard Launcher 出现

↓

搜索 / 浏览历史

↓

选择内容

↓

Enter

↓

立即粘贴

↓

窗口立即消失

整个过程应该：

Fast
Quiet
Natural
Predictable

用户不应该感觉自己打开了一个“软件”。

而应该感觉：

“Windows 多了一个更好用的剪贴板。”

---

# 3. UI 核心原则

必须遵循：

1. 极简
2. 高信息密度
3. 强视觉层级
4. 键盘优先
5. 鼠标辅助
6. 动画克制
7. 原生感
8. 快速响应
9. 不打扰用户
10. 不做传统后台管理系统风格

---

# 4. 禁止的 UI 风格

禁止：

- Element UI 风格
- Bootstrap Dashboard
- 普通后台管理系统
- 大量表格
- 大量按钮
- 巨大的 Card
- 巨大的标题
- 过度圆角
- 五颜六色
- 霓虹灯
- 科技大屏风格
- 复杂渐变
- 过度阴影
- 过度毛玻璃
- 花哨动画

不要做成：

“企业管理软件”。

---

# 5. 主窗口

主窗口不是传统最大化窗口。

默认：

Floating Window

尺寸建议：

宽：

720px ~ 900px

高：

520px ~ 700px

根据内容动态调整。

窗口：

- 圆角
- 无明显传统 Window Border
- 轻微阴影
- Mica / Acrylic
- 支持 Dark / Light
- 支持 Windows 11 圆角

---

# 6. Launcher

Launcher 是整个产品最重要的 UI。

视觉结构：

┌─────────────────────────────────────┐
│                                     │
│   🔍   Search clipboard...          │
│                                     │
├─────────────────────────────────────┤
│                                     │
│   Today                             │
│                                     │
│   ┌─────────────────────────────┐   │
│   │ Text                        │   │
│   │ Hello World                 │   │
│   │ copied 2 min ago            │   │
│   └─────────────────────────────┘   │
│                                     │
│   ┌─────────────────────────────┐   │
│   │ Image                       │   │
│   │ [ image thumbnail ]          │   │
│   └─────────────────────────────┘   │
│                                     │
└─────────────────────────────────────┘

但是不要机械复制这个 ASCII 布局。

最终 UI 应该具有真实产品感。

---

# 7. Search Bar

搜索框是 Launcher 的视觉核心。

必须：

- 默认自动获得焦点
- 光标明显
- 支持键盘
- 支持实时搜索
- 支持 debounce
- 支持搜索取消
- 支持搜索高亮

Placeholder：

Search clipboard...

不要使用：

“请输入搜索内容”。

---

# 8. Search UI

用户输入：

rabbit

显示：

RabbitMQ configuration

RabbitMQ consumer

RabbitMQ connection

搜索结果需要高亮：

rabbit

但不要使用非常刺眼的高亮颜色。

---

# 9. Clipboard Item

Clipboard Item 根据类型拥有不同视觉表现。

类型：

Text

Image

URL

Code

File

HTML

RTF

---

# 10. Text Item

Text 类型：

┌───────────────────────────────┐
│ T                             │
│                               │
│ This is a clipboard text...   │
│                               │
│ 2 minutes ago                 │
└───────────────────────────────┘

显示：

- 内容摘要
- 类型
- 时间
- 来源 App
- Favorite 状态

内容太长时：

不要无限撑高。

使用：

Line Clamp

---

# 11. Image Item

Image：

必须显示 Thumbnail。

例如：

┌───────────────────────────────┐
│                               │
│       IMAGE                   │
│                               │
│       [ thumbnail ]            │
│                               │
│ copied 5 minutes ago          │
└───────────────────────────────┘

图片不能变形。

必须：

object-fit: contain

图片背景根据：

Light / Dark

自动调整。

---

# 12. URL Item

URL 必须显示：

网站 Icon

Title

Domain

URL

例如：

┌────────────────────────────────┐
│ 🌐 GitHub                      │
│ github.com                     │
│ https://github.com/...         │
└────────────────────────────────┘

---

# 13. Code Item

代码内容不能像普通 Text 一样显示。

需要：

Monospace Font

Syntax-like Visual Treatment

例如：

┌────────────────────────────────┐
│ {                              │
│   "name": "Clipboard"          │
│   "version": "1.0"             │
│ }                              │
└────────────────────────────────┘

但第一阶段不需要实现完整 Syntax Highlight。

重点是：

“看起来像代码”。

---

# 14. File Item

文件：

显示：

File Icon

File Name

Extension

Size

Path

例如：

┌────────────────────────────────┐
│ 📄 report.pdf                  │
│ PDF · 2.4 MB                   │
│ Documents / Report             │
└────────────────────────────────┘

---

# 15. Item Hover

Hover 时：

轻微背景变化。

不要：

巨大缩放。

不要：

明显发光。

不要：

弹出巨大 Tooltip。

Hover 动画：

100ms ~ 180ms

---

# 16. Selected Item

键盘导航选中的 Item：

必须非常明显。

例如：

轻微背景变化

+ 左侧 Indicator

或者：

Subtle Border

不要使用：

传统蓝色 Windows Selection。

视觉应该现代。

---

# 17. Keyboard Navigation

必须支持：

↑

↓

Enter

Escape

Delete

Cmd/Ctrl + F

Home

End

---

# 18. 快捷操作

选中 Item 后：

Enter

= Paste

Shift + Enter

= Paste as Plain Text

Delete

= Delete

Space

= Preview

F

= Favorite

---

# 19. Command Hint

底部可以显示：

Enter Paste

Shift Enter Plain Text

F Favorite

Delete Remove

Escape Close

设计要非常克制。

不要做成传统 Toolbar。

---

# 20. Favorite

Favorite 不应该占据大量 UI。

默认：

隐藏。

Hover 时：

显示 Star。

Favorite：

使用非常克制的视觉强调。

---

# 21. Sidebar

不要默认显示巨大 Sidebar。

推荐：

Launcher 默认：

无 Sidebar

如果进入完整历史：

可以使用：

左侧窄 Sidebar

例如：

All

Favorites

Images

Links

Files

Code

---

# 22. Sidebar

Sidebar 宽：

180px ~ 220px。

Item 高度：

32px ~ 38px。

不能做成：

传统后台菜单。

---

# 23. Header

Header 不应该巨大。

高度：

48px ~ 56px。

主要内容：

Search

Filter

Window Controls

---

# 24. 完整历史页面

完整 History 页面可以：

左：

分类

中：

Clipboard List

右：

Preview

布局：

Sidebar
+
List
+
Preview

但 Launcher 模式：

只显示：

Search + List

---

# 25. Preview

点击 / Space：

显示 Preview。

Preview 支持：

Text

Image

Code

URL

File

---

# 26. Preview Animation

Preview：

轻微 Fade + Slide

时间：

150ms ~ 220ms

不要：

弹簧动画过强。

不要：

缩放动画过大。

---

# 27. Empty State

没有剪贴板历史：

不要：

“暂无数据”。

应该：

设计产品化 Empty State。

例如：

Clipboard history is empty

Copy something to get started.

可以提供：

快捷键提示。

---

# 28. Search Empty State

搜索不到：

不要：

“暂无数据”。

显示：

No results

Try another keyword.

---

# 29. Loading

不能出现：

巨大 Loading Spinner。

优先：

Skeleton

或者：

局部 Loading。

---

# 30. Error

错误提示：

必须：

简洁。

例如：

Couldn't read clipboard.

Try again.

不要：

弹 Java Exception。

不要：

显示 Stack Trace。

---

# 31. Toast

Toast 只用于：

- Copy
- Delete
- Favorite
- Error

例如：

Copied

Deleted

Added to Favorites

显示：

1.5 ~ 2 秒

不要：

长期存在。

---

# 32. Settings UI

Settings 不要做传统：

Form + Table。

推荐：

左侧分类：

General
Clipboard
Appearance
Shortcuts
Privacy
Storage
About

右侧：

Settings Content

---

# 33. Settings Item

例如：

Global Shortcut

Ctrl + Shift + V

[ Change ]

这种设计。

---

# 34. Dark Mode

Dark Mode 是主要模式之一。

背景：

非常深。

但不要：

纯黑 #000000。

推荐：

接近：

#1C1C1E

或者根据 Windows System Theme 自动选择。

---

# 35. Light Mode

Light Mode：

背景：

接近：

#F5F5F7

卡片：

接近：

#FFFFFF

不要：

纯白 + 黑边。

---

# 36. Color System

不要随意使用颜色。

建立：

Primary

Background

Surface

SurfaceHover

Border

TextPrimary

TextSecondary

TextTertiary

Danger

Success

Accent

所有颜色必须通过 Design Token 管理。

---

# 37. Typography

优先使用 Windows 系统字体：

Segoe UI Variable

中文：

Microsoft YaHei UI

字体层级：

Title

Body

Caption

Mono

不要使用：

过度艺术字体。

---

# 38. 字体大小

推荐：

Title：

20 ~ 24px

Body：

14 ~ 15px

Secondary：

12 ~ 13px

Caption：

11 ~ 12px

不要大面积使用：

18px+

---

# 39. 圆角

推荐：

Window：

16px ~ 20px

Card：

10px ~ 14px

Button：

8px ~ 10px

不要：

所有元素 20px。

---

# 40. 间距

使用：

4px Grid。

例如：

4
8
12
16
20
24
32

避免：

17px

23px

29px

这种随意数字。

---

# 41. Animation

动画必须：

Fast

Subtle

Functional

推荐：

100ms

150ms

200ms

300ms

禁止：

> 500ms 的普通 UI 动画。

---

# 42. Window Animation

Launcher 打开：

轻微：

Opacity

Scale

Translate

但动画必须非常快。

用户按快捷键后：

应该感觉：

“瞬间出现”。

---

# 43. Shadow

阴影：

Subtle。

不要：

明显黑色阴影。

不要：

Material Design 那种厚重阴影。

---

# 44. Mica / Acrylic

Windows 11：

优先 Mica。

特殊浮动场景：

可以 Acrylic。

Windows 10：

提供兼容方案。

如果透明效果影响性能：

自动降低效果。

性能优先。

---

# 45. Responsive

必须支持：

1366 × 768

1920 × 1080

2560 × 1440

3840 × 2160

同时支持：

100%

125%

150%

175%

200%

---

# 46. 多显示器

Launcher：

优先出现在：

当前活动窗口所在屏幕。

如果无法判断：

使用鼠标所在屏幕。

禁止固定：

Primary Monitor。

---

# 47. 高 DPI

禁止：

硬编码：

1920x1080

禁止：

根据分辨率计算 UI。

应该使用：

DPI-aware Layout。

---

# 48. UI Performance

必须保证：

Scrolling：

60 FPS 目标。

Search：

快速响应。

Window Open：

尽可能低延迟。

不要因为：

Blur

Shadow

Animation

造成明显卡顿。

---

# 49. Virtualization

Clipboard List：

必须支持：

UI Virtualization。

即使：

100,000 条历史

也不能：

一次性创建全部 UI。

---

# 50. Image Performance

List：

只显示 Thumbnail。

Preview：

才加载 Original。

禁止：

List 加载原始大图。

---

# 51. Accessibility

支持：

Keyboard Navigation

Focus

Screen Reader

High Contrast

---

# 52. Focus

Launcher 打开：

SearchBox 自动 Focus。

用户：

Escape

立即关闭。

焦点不能丢失。

---

# 53. Context Menu

右键 Clipboard Item：

显示：

Paste

Paste as Plain Text

Favorite

Copy

Delete

Open

但菜单必须：

简洁。

---

# 54. Drag & Drop

未来支持：

拖拽文件到 Clipboard Manager。

第一阶段可以不实现。

架构必须预留。

---

# 55. Design Tokens

所有 UI 不允许：

到处硬编码颜色。

例如：

不要：

Background="#123456"

应该：

Background="{StaticResource SurfaceBrush}"

必须建立：

Colors

Typography

Spacing

CornerRadius

Shadow

Animation

等 Design Tokens。

---

# 56. Component Architecture

建议：

Views

Components

Controls

Styles

Resources

Themes

例如：

UI/

├── Views/

├── Components/

│   ├── ClipboardItem

│   ├── SearchBox

│   ├── Sidebar

│   ├── PreviewPanel

│   └── CommandBar

├── Styles/

├── Themes/

└── Resources/

---

# 57. ClipboardItem Component

必须高度复用。

不要：

TextItem

ImageItem

UrlItem

FileItem

分别复制大量代码。

应该：

ClipboardItem

根据 Type：

选择不同 Template。

---

# 58. Data Template

使用：

DataTemplate

DataTemplateSelector

实现：

Text

Image

URL

File

Code

不同 UI。

---

# 59. UI 状态

ClipboardItem 必须支持：

Normal

Hover

Selected

Focused

Favorite

Deleting

Loading

Error

---

# 60. UI 状态不要互相冲突

例如：

Selected + Favorite

Hover + Selected

Disabled + Hover

必须正确处理。

---

# 61. Design Review

每完成一个 UI 页面：

必须检查：

- 对齐
- 间距
- 字体
- 层级
- 圆角
- 阴影
- 动画
- Hover
- Focus
- Dark Mode
- Light Mode
- DPI
- Keyboard

---

# 62. AI Coding UI 规则

AI 在开发 UI 时：

禁止直接大规模生成一个 1000+ 行 XAML。

应该拆分：

SearchBox

ClipboardItem

ClipboardList

Preview

Sidebar

Settings

等组件。

---

# 63. AI 修改 UI 的流程

必须：

1. 阅读 UI_DESIGN_PROMPT.md
2. 查看现有 UI
3. 查看 Design Tokens
4. 找到相关组件
5. 最小修改
6. Build
7. 运行测试
8. 检查 Dark Mode
9. 检查 Light Mode
10. 检查 DPI

---

# 64. UI 验收标准

完成 UI 后必须满足：

视觉：

Modern
Native
Minimal
Professional

体验：

Fast
Keyboard First
Predictable

性能：

Low CPU
Low Memory
Smooth Scrolling

---

# 65. 最重要的原则

永远不要问：

“怎样把这个 UI 做得更复杂？”

应该问：

“怎样用更少的 UI 表达更多的信息？”

产品应该：

Less UI
More Information
Less Interaction
More Efficiency

---

# 66. 最终视觉目标

用户看到 Launcher 时：

第一感觉：

“这是一个高级的原生 Windows 工具。”

而不是：

“这是一个 Vue/React 做的后台页面。”

最终体验目标：

按快捷键

↓

Launcher 快速出现

↓

搜索 / ↑↓

↓

Enter

↓

粘贴

↓

Launcher 消失

整个过程：

快速

自然

安静

无打扰。

# END