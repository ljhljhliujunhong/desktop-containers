# 澄格 — AI 接手说明

这份文档给下一个接手本仓库的 AI。产品需求在同目录的 `需求文档.md`。这里只写已经落地的实现、约束和改哪里。

## 产品是什么

Windows 桌面上始终展开的应用分组。用户建一个有名字的容器，把快捷方式拖进去，容器里直接显示原来的应用图标，双击启动。可以在容器之间拖，也可以拖回桌面。调整大小后图标自动换行。每个容器单独换外观。布局在重启和开机后还在。

它不是文件夹，也不是壁纸窗口。图标必须能点，所以容器要盖在桌面图标上面，又要躲在普通窗口下面。

另有一个管理窗口：首页、容器、主题、设置。第一次打开会先问容器名字和主题。托盘可以打开管理窗口、开关编辑模式、新建容器、退出。关掉管理窗口只是隐藏，托盘里的「退出」才结束进程。

界面对用户显示的产品名是「澄格」（代码里的 `Brand.Name`）。标志由 `Views\BrandIcon.cs` 画出来：粉紫圆角底、白卡片、一条粉标题和三个色点。窗口图标、托盘图标和 exe 图标都用它。exe 图标文件是 `src\DesktopContainers\Assets\app.ico`，由 `dotnet run -- --export-icon <路径>` 生成后再编译进去。

界面文案是中文。视觉是浅纸色 `#FFF7F4`、强调色 `#E85A8C`、大圆角。滚动条是 `App.xaml` 里的细圆角样式，没有箭头按钮。不要把需求文档、设计原则或操作教程写到界面上。删除确认可以在当下说明后果，现有文案是「能用的快捷方式会回到桌面，程序不会被卸掉。」空容器只在没有任何应用时显示「拖到这里」。找不到目标时显示「找不到」。

## 技术选择

- C# / .NET 8 WPF（`net8.0-windows`），只用 WinForms 的 `NotifyIcon` 和 `Screen`。没有第三方 NuGet 包。
- 界面几乎全是代码搭的。XAML 只有 `App.xaml`。要改外观，看 `Views\UiKit.cs`、`Views\ManagerPages.cs`、`Views\ContainerWindow.cs`。
- 数据是 `%AppData%\DesktopContainers` 下的 JSON 和快捷方式文件。
- 日常编译是依赖本机运行时的 Release。给用户双击和开机启动用的是自包含发布，输出在仓库根目录的 `app\`（已被 gitignore）。登录时系统不会带上 `DOTNET_ROOT`，所以注册表必须指向自包含的 exe。

不要把容器 `SetParent` 到 `Progman` 或 `WorkerW`。这台机器是 Windows `10.0.26200`（Win11 24H2 及以后）。`Progman` 带 `WS_EX_NOREDIRECTIONBITMAP`，透明 WPF 子窗口经常画不出来，也收不到点击。当前做法是顶层分层窗口（`AllowsTransparency` + `WindowStyle.None`），加上 `WS_EX_TOOLWINDOW`，不进任务栏和 Alt-Tab。用 `SetWindowPos` 和 `EVENT_SYSTEM_FOREGROUND` 钩子，把容器压在 `Progman` 上面、普通窗口下面。

`WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE`（3），不要返回 `MA_NOACTIVATEANDEAT`。不要加 `WS_EX_NOACTIVATE`，否则 OLE 拖放会坏。`SetWindowPos` 只改位置时必须带 `SWP_NOZORDER`：`HWND_TOP` 的值是 `IntPtr.Zero`，漏了这个标志会把窗口甩到最前。

`AllowsTransparency` 会走软件渲染。不要加 `DropShadowEffect`。阴影是两层低透明度圆角边。动画用 `FillBehavior.HoldEnd`，结束后清掉时钟，空闲时 CPU 应接近 0。背景透明度是 0 到 1 的连续值。滑到 0 时，空白处会点穿到桌面，边框和图标仍然能点。

显示桌面时拒绝 `SC_MINIMIZE`，也拒绝 `WINDOWPOS` 的 x/y 为 `-32000`。这不能 100% 挡住 Win+D，不要在用户机器上模拟 Win+D 做测试。

## 目录

```
桌面容器\
  澄格.exe                  用户双击这个文件启动。单文件自包含发布，已 gitignore
  启动桌面容器.cmd          转去启动根目录的 澄格.exe
  app\                      同一份 exe 的副本，文件名仍是 DesktopContainers.exe，gitignore
  docs\需求文档.md
  docs\AI接手说明.md
  src\DesktopContainers\
    DesktopContainers.csproj
    Assets\app.ico          exe 图标，改标志后要重新导出再编译
    app.manifest            长路径；DPI 由项目属性 ApplicationHighDpiMode=PerMonitorV2 负责
    App.xaml / App.xaml.cs  无 StartupUri；滚动条样式在 App.xaml；异常时先 Flush 布局
    NativeMethods.cs
    Models.cs               模型、主题、颜色、JSON 选项
    SmokeTest.cs
    Services\               状态、存储、快捷方式、图标、层级、显示器、单实例、开机启动
    Views\                  容器窗口、管理窗口、托盘、拖放、对话框、标志
```

## 编译和运行

SDK 不在系统 PATH 上。用 `E:\VsCodeProject\AgentCache\dotnet` 里的 8.0.425，并且不要改用户 PATH。每次在 PowerShell 里先设：

```powershell
$env:DOTNET_ROOT = 'E:\VsCodeProject\AgentCache\dotnet'
$env:PATH = 'E:\VsCodeProject\AgentCache\dotnet;' + $env:PATH
$env:DOTNET_CLI_HOME = 'E:\VsCodeProject\AgentCache\dotnet-home'
$env:NUGET_PACKAGES = 'E:\VsCodeProject\AgentCache\package-caches\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:TEMP = 'E:\VsCodeProject\AgentCache\temp\desktop-containers\tmp'
$env:TMP = $env:TEMP
```

编译：

```powershell
& "$env:DOTNET_ROOT\dotnet.exe" build 'E:\VsCodeProject\桌面容器\src\DesktopContainers\DesktopContainers.csproj' -c Release
```

冒烟（会在桌面上闪几下窗口，然后自己退出；数据写在 AgentCache，不会动用户的桌面图标，也不要去按 Win+D）：

```powershell
& "$env:DOTNET_ROOT\dotnet.exe" run --project 'E:\VsCodeProject\桌面容器\src\DesktopContainers\DesktopContainers.csproj' -c Release -- --smoke --data 'E:\VsCodeProject\AgentCache\temp\desktop-containers\smoke'
```

`--data` 路径必须包含 `AgentCache`，否则冒烟直接拒绝。退出码 0 才算过。报告在该目录的 `smoke-report.txt`，截图是 `manager-*.png`、`onboarding.png`、`container.png`、`container-screen.png`。冒烟会导入 `notepad.exe` 做图标检查，因为记事本不在桌面上，删容器时也不会把快捷方式倒回真实桌面。

改了标志以后，先导出 ico，再带上 `ApplicationIcon` 重新编译：

```powershell
& "$env:DOTNET_ROOT\dotnet.exe" run --project 'E:\VsCodeProject\桌面容器\src\DesktopContainers\DesktopContainers.csproj' -c Release -- --export-icon 'E:\VsCodeProject\桌面容器\src\DesktopContainers\Assets\app.ico'
```

发布单文件 exe。用户双击的是项目根目录的 `澄格.exe`，所以每次改完代码都要重新发布并覆盖它，同时覆盖 `app\DesktopContainers.exe`，避免开机启动还指着旧文件。发布前先关掉正在运行的澄格，否则文件会被锁住。

```powershell
$stage = 'E:\VsCodeProject\AgentCache\temp\desktop-containers\publish-single'
& "$env:DOTNET_ROOT\dotnet.exe" publish 'E:\VsCodeProject\桌面容器\src\DesktopContainers\DesktopContainers.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $stage
Copy-Item "$stage\DesktopContainers.exe" 'E:\VsCodeProject\桌面容器\澄格.exe' -Force
New-Item -ItemType Directory -Force -Path 'E:\VsCodeProject\桌面容器\app' | Out-Null
Copy-Item "$stage\DesktopContainers.exe" 'E:\VsCodeProject\桌面容器\app\DesktopContainers.exe' -Force
```

然后双击仓库根目录的 `澄格.exe`。

命令行：

| 参数 | 作用 |
|---|---|
| `--data <目录>` | 数据根目录。也可用环境变量 `DESKTOP_CONTAINERS_HOME` |
| `--startup` | 开机启动：只显示容器，不打开管理窗口和首次引导 |
| `--smoke` | 冒烟测试，跑完退出 |

`UseWindowsForms` 会注入 `System.Drawing` 和 `System.Windows.Forms` 的全局 using，和 WPF 的 `Color`、`Button`、`Image` 撞名。项目文件里已经 `Using Remove` 掉这两个，并补了 `System.IO`（这个 SDK 的隐式 using 不含 IO）。新文件不要再全局引入 WinForms。需要 `Screen` 或 `NotifyIcon` 时写全名。

## 数据

默认目录：`%AppData%\DesktopContainers`。

```
layout.json          当前布局
layout.json.bak      上一次成功写入的副本
backups\layout-*.json  最多留 8 份
shortcuts\{id}.lnk   或 .url，扁平存放
logs\app.log
```

保存是临时文件 + `File.Replace`，再留 `.bak`。读失败先试 `.bak`，再试 `backups`。JSON 是 camelCase、缩进、`UnsafeRelaxedJsonEscaping`（中文可读）、字符串枚举，`version` 为 1。

位置同时存 WPF DIP（`x` `y` `width` `height`）和物理像素（`pixelX` `pixelY` `hasPixelPosition`）以及 `monitorDevice`。还原用 `SetWindowPos` + `SWP_NOZORDER`。可见区域小于 80×80 物理像素时夹回最近的工作区。

快捷方式规则：

- 拖入 `.lnk` / `.url` 时先复制进 `shortcuts`，条目写进容器之后，才删除桌面上的那一个快捷方式。
- 拖入 `.exe` 时在库里新建一个指向它的 `.lnk`，不删除 exe。
- 只删除桌面上的 `.lnk` / `.url`。不删除 exe，也不删除库和桌面以外的文件。
- 删容器时，能用的快捷方式移回桌面；这一步有记录，中途失败会回滚。已经丢失目标的，只删库里自己的那个快捷方式。
- 拖出容器且落点不在任何容器上，等于移回桌面。
- 同一个目标已经在另一个没锁定的容器里时，移动条目，并删掉新拖进来的那份副本。
- `.url` 和空目标不算丢失。带盘符或斜杠、但磁盘上不存在的路径才算丢失。

图标：先解析 `.lnk` / `.url` 的 `IconLocation` 或目标，再用 `SHDefExtractIconW` 抽到 256 像素，失败再退回 `SHGetFileInfo`。缓存键是路径 + 修改时间 + 尺寸。位图要 Freeze。抽出的 `HICON` 必须 `DestroyIcon`。

## 交互

编辑模式是全局的，在管理窗口侧栏、托盘、设置里切换。管理窗口的「容器」页是左右两栏：左边点一个已有容器，右边编辑这一只的名称、图标、标题、锁定、颜色、透明度，以及里面应用的上移、下移、移出、移到别的容器。首页点预览，或在桌面容器上点「外观」，都会选中那一只并打开这一页。首页「全部容器」可以统一透明度、标题、图标名称和图标大小。勾上才覆盖桌面；标题和图标名称旁边是「显示 / 隐藏」。各自在容器页里的值还留着，取消勾选就回到各自的值。不要把这些统一值写进容器本身。

「主题」页只准备以后新建的容器。改预设会记在 `settings.themeEdits` 里，不会写进已经放在桌面上的容器。点预设只换选中状态，格子大小固定，不会把旁边的卡片挤走。新容器对话框会把管理窗口罩暗，卡片本身有粉色描边。容器页里这一只的「图标」，以及设置里的「新容器图标」，都是小、中、大三档，和桌面容器菜单里的图标大小一样。「导出备份 / 导入备份」是把布局存成文件，或用一份备份换掉当前布局。

- 普通模式：双击启动，可以往容器里丢文件，右键菜单可用，也可以拖动容器位置。不能改图标顺序，不能缩放。
- 编辑模式：拖容器、右下角缩放、容器之间拖图标、拖到外面回到桌面、标题双击改名。缩放区域没有图标，只有透明命中区和斜向光标（`CornerGrip`）。
- 锁定的容器仍可启动，但不能改布局，也不能拖动。往里丢文件在未锁定时一直可以。
- 拖容器的起点不能是图标、按钮或缩放区域。移动超过约 6 像素才开始拖。图标拖动阈值是 8 像素，而且只在编辑模式、未锁定时。
- 拖放自定义格式是 `DesktopContainers.AppDrag`，内容是 `containerId|appId`。`DragSession` 把真正的 `MoveApp` 推迟到 `DoDragDrop` 返回之后。只有没被某个容器吃掉、没取消、并且光标不在任何容器里，才移回桌面。同一容器里重排时，先移除再按新索引插入。
- 新容器默认按 3 列错开（约 468×344 DIP）。若仍和已有容器大面积重叠，`MonitorGuard.NudgeApart` 再挪。已保存过像素位置的容器不再自动挪开。
- 容器大小按里面的图标格子对齐。最宽是全部排成一行，最高是全部排成一列，中间只能停在完整的列数上。没有图标时保持 440×320。容器里不出现滚动条。图标尺寸：小 40、中 56、大 80，滑条 32–96。
- 标题位置：`Top` `Bottom` `TopLeft` `Center` `Hidden`。
- 布局变更走 `RequestSave`，400ms 防抖；删除、导入、退出、关机用 `Flush`。

## 主题

内置 10 个，默认 `strawberry-milk`：

| id | 名称 |
|---|---|
| strawberry-milk | 草莓牛奶 |
| sky-blue | 天空蓝 |
| mint | 薄荷绿 |
| lemon | 柠檬黄 |
| peach | 蜜桃粉 |
| lavender | 薰衣草紫 |
| cream | 奶油白 |
| glass | 玻璃透明 |
| aurora | Aurora |
| candy | Candy |

自定义主题 id 以 `custom-` 开头，存在 `layout.json` 的 `customThemes`。删自定义主题时，已经用过它的容器外观不变。确认文案是「已经用过它的容器不会变。」

## 单实例、开机启动、DPI

- 互斥量：`Local\DesktopContainers_` + 数据目录哈希 + `_m`。事件是同一个前缀 + `_e`。上次崩溃留下的 abandoned mutex 直接接手。第二个实例发信号，第一个把管理窗口显示出来。
- 开机启动：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值名 `DesktopContainers`，内容是 `"exe" --startup`。设置项是准的；启动时按设置写回注册表。
- DPI：`ApplicationHighDpiMode` 为 `PerMonitorV2`。不要在 `app.manifest` 里再写 `dpiAware` / `dpiAwareness`，SDK 会警告并剥掉。鼠标拖动的像素差用 `TransformFromDevice` 的 M11/M22 换回 DIP。

## 改功能时先看哪里

| 想改的东西 | 文件 |
|---|---|
| 启动顺序、托盘、首次引导、退出 | `Services\AppHost.cs`、`App.xaml.cs` |
| 创建、删除、移动、导入、主题、保存 | `Services\AppState.cs` |
| 快捷方式复制、删除、还原、启动 | `Services\ShortcutService.cs` |
| 图标抽取 | `Services\IconService.cs` |
| 谁在桌面图标上面 | `Services\DesktopPlacement.cs` |
| 多显示器夹取、新容器错开 | `Services\MonitorGuard.cs` |
| 容器外观、拖动和按格子缩放 | `Views\ContainerWindow.cs`、`Views\CornerGrip.cs` |
| 产品名和标志 | `Views\BrandIcon.cs` |
| 图标单击、双击、拖出 | `Views\AppIconView.cs`、`Views\DragSession.cs` |
| 管理窗口页面 | `Views\ManagerWindow.cs`、`Views\ManagerPages.cs` |
| 首次引导 / 新建 | `Views\NewContainerWindow.cs` |
| 按钮、输入框、滑条 | `Views\UiKit.cs` |
| 颜色和 10 个主题 | `Models.cs` 里的 `ThemeCatalog`、`Paint` |
| 冒烟 | `SmokeTest.cs` |

管理窗口侧栏是 `DockPanel`：先加入 `Dock.Bottom` 的底部栏，再加入导航，导航才会填满剩余高度。不要把底部栏加第二次，否则运行时会报「Visual 已经是另一个 Visual 的子级」。

`ShortcutService.Import` 不能改回 `ImportOne`。类名已经叫 `ImportOne`，方法同名会把 `ImportOne.Fail` 解析成方法。

## 不要做的事

- 不要把需求说明、原则、教程写进界面。
- 不要全局安装 SDK、NuGet、Python、Node。缓存根目录是 `E:\VsCodeProject\AgentCache`。
- 不要 `SetParent` 到桌面窗口。
- 不要给透明窗口加 `DropShadowEffect` 或 `WS_EX_NOACTIVATE`。
- 不要在冒烟或测试里删除用户桌面上的快捷方式，也不要模拟 Win+D。
- 不要卸载或删除用户的 exe。删除容器只移动本程序拥有的快捷方式。
- 用户没明确要求时不要提交 git。`澄格.exe`、`app\`、`bin\`、`obj\` 不要提交。

## 已知限制

- 没有资源管理器的桌面右键扩展。拖进容器、从容器拖出，是本程序窗口上的拖放。
- 「显示桌面」只能尽量不让容器被最小化，不能保证所有系统版本都一样。
- 透明容器是软件渲染，窗口多时比普通 WPF 窗口更吃 CPU，但空闲时应停在接近 0。
- 冒烟只证明：容器在 `Progman` 之上、记事本图标能抽出、快捷方式进了库且原 exe 还在、重读布局得到 2 个容器。它不覆盖拖放、锁定、删除回桌面、开机启动注册表。
- 层级若再出问题：冒烟里 `above=false`，或屏幕截图被别的窗口整块挡住。可以改成贴在 `Progman` 和顶层 `WorkerW` 中更高的那个之上，仍然不要 `HWND_TOP` 盖住用户正在用的程序。`container.png` 是 `RenderTargetBitmap`，看控件本身；`container-screen.png` 是 `CopyFromScreen`，看它在桌面上的真实叠放。
