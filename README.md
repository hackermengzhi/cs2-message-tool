# CS2 消息工具（WPF / C# / .NET 10）

准备消息 → 用户手动发送。所有热键只更新 cfg；无进程注入、游戏内存读取、键盘钩子、SendInput 或自动输入。

## 使用

1. Windows 10/11 x64 安装 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)，运行 `Windows-x64/CS2MessageTool.exe`。保留该文件夹全部文件。
2. 在配置窗口把 `CfgDirectory` 改为本机实际的 `...\Counter-Strike Global Offensive\game\csgo\cfg` 绝对路径。JSON 中反斜杠须写为 `\\`，也可用 `/`。工具不自动查找 Steam，也不创建游戏文件夹。
3. 点“打开词库文件夹”，编辑 `kill.txt`、`death.txt`、`short.txt`，一行一条，保存 UTF-8。词库在“保存并应用”时重新载入，不会实时监视文件。
4. 点“保存并应用”，确认热键注册成功。启动和应用配置不写游戏 cfg，首次必须按准备热键。
5. 游戏控制台手动添加以下绑定（J/K/L 会替换这些键原先的游戏绑定，请自行选择空闲键）：

```cfg
bind "j" "exec trash_kill.cfg"
bind "k" "exec trash_death.cfg"
bind "l" "exec trash_short.cfg"
```

| 词库 | 工具准备键 | 游戏内手动发送键 | 默认模式 |
|---|---|---|---|
| kill | Ctrl+Alt+F6 | J | Random |
| death | Ctrl+Alt+F7 | K | Random |
| short | Ctrl+Alt+F8 | L | Sequential |

例如先按 Ctrl+Alt+F6，看到“已写入 trash_kill.cfg”，松开修饰键，再按 J。不要将准备键和发送键设为同一按键；全局热键可能被 Windows 消费，且文件写入与游戏读取没有同键时序保证。工具无法知道你是否已按 J；再次准备会替换此前未发送的内容。连续按 J 会重复发送当前 cfg 内容。

绑定持久化可由用户添加到自己的 autoexec；工具不修改 autoexec、不主动执行控制台命令、不发送消息。默认示例词库只有友好短句，可以自行替换。

## 配置

配置和词库保存在 `%LOCALAPPDATA%\CS2MessageTool`。首次生成默认 JSON；已有文件不覆盖。配置窗口直接编辑 JSON，支持添加/删除 profiles（1–12 个）。

```json
{
  "CfgDirectory": "D:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo/cfg",
  "OverlayLeft": 30,
  "OverlayTop": 80,
  "OverlayWidth": 460,
  "OverlayOpacity": 0.88,
  "Profiles": [
    {
      "Name": "kill",
      "Hotkey": "Ctrl+Alt+F6",
      "Library": "kill.txt",
      "CfgFile": "trash_kill.cfg",
      "Mode": "Random",
      "CooldownMs": 1000
    }
  ]
}
```

- `Mode`：`Sequential` 按行轮换；`Random` 洗牌词袋，一轮内每条一次，跨轮避免相邻重复。单条词库只能重复。
- 词库去空行、去两端空白、按文本完全一致去重；去重范围是各自词库。
- `Library` 可用绝对路径；相对路径基于上述数据文件夹。
- `Hotkey` 使用 WPF Key 名称，如 `Ctrl+Alt+F6`、`Ctrl+Shift+D1`。至少一个 Ctrl/Alt/Shift，拒绝 Win、F12 和单独修饰键。冲突时报错并尝试恢复旧注册。
- 冷却按每个词库独立计算，从成功写入起计；按键自动连发由 `MOD_NOREPEAT` 抑制。暂停期间热键仍注册但不写入。退出解除注册。
- 配置保存成功后才替换活动配置；失败保留旧配置。若恢复旧注册失败，明确显示错误并暂停。
- 每次应用、重启重建消息队列，重置冷却和本次运行的“已写入”记录。词库顺序/历史不保存。磁盘 cfg 保留上次内容。
- 悬浮框坐标和透明度在配置中调整，不能拖动，鼠标穿透且不激活；配置窗口可最小化，关闭则退出。没有托盘图标。较多词库可增大宽度；超过屏幕/700 DIP 高度的预览可能裁切。
- 全局热键在其他应用中也有效，可用配置窗口暂停；工具不检查 CS2 是否处于前台。

## 写入策略与边界

输出为 UTF-8 无 BOM，例如 `say "好枪"` 加 CRLF。先写同目录临时文件并 flush，再替换目标文件；失败不推进队列、不开始冷却，不以截断方式覆盖正在使用的 cfg。

为避免把消息解析成额外控制台命令，拒绝双引号、反斜杠、分号、`//` 和控制字符，而非猜测 CS2 的转义规则。每条最多 120 个 UTF-8 字节（工具的保守上限，不代表游戏协议上限）。cfg 文件名只允许 ASCII 字母、数字、横线、下划线及 `.cfg`，每个词库独立输出文件。请给本工具使用专用名称；对应文件会被有意替换。

启动不会检查写权限；按准备键时若游戏目录权限或文件占用导致失败，会显示错误。请选择可正常写入的游戏库路径。若暂停或写入失败，游戏 exec 仍可能读取之前保留的 cfg，只有“已写入”成功状态才代表本次内容已落盘。

普通桌面悬浮窗在独占全屏中可能不可见；请在 CS2 的窗口化/无边框窗口模式进行验收。

## 项目结构与关键实现

```text
CS2MessageTool/
  Core/
    Engine.cs          配置校验、UTF-8、原子写入、队列、冷却
    Core.csproj
  Desktop/
    Program.cs         应用入口、单实例、配置窗口、保存/应用/回滚
    Native.cs          RegisterHotKey / WM_HOTKEY、悬浮窗样式
    Overlay.cs         WPF 透明置顶预览窗口
    GlobalUsings.cs
    Desktop.csproj
  Checks/
    Program.cs         核心行为可执行检查
    Checks.csproj
  Windows-x64/         已发布文件（需 .NET 10 Desktop Runtime）
  README.md
```

UI 使用 C# 构建 WPF 控件，无 XAML，无第三方运行依赖。完整关键代码均在上述文件。

热键注册使用 Win32 API，不安装输入钩子：

```csharp
Native.RegisterHotKey(handle, id, modifiers | 0x4000, virtualKey);
// 0x4000 = MOD_NOREPEAT；通过 HwndSource hook 接收 WM_HOTKEY (0x0312)。
```

处理消息先落盘再推进：

```csharp
var message = Queue.Next;
Storage.AtomicWrite(Path.Combine(directory, Profile.CfgFile), Storage.Say(message));
Current = message;
writtenAt = Stopwatch.GetTimestamp();
Queue.Commit();
```

透明窗口使用 `AllowsTransparency=true`、`Background=Transparent`、`Topmost=true`、`ShowActivated=false`，并设置 `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`。点击穿透依赖原生 layered window 样式，不能仅靠 WPF `IsHitTestVisible=false`。

官方依据：[RegisterHotKey 与 MOD_NOREPEAT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)、[WPF AllowsTransparency](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency)、[分层窗口的鼠标穿透](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)。

## 构建

Windows 安装 .NET 10 SDK，在本目录执行：

```powershell
dotnet build Desktop/Desktop.csproj -c Release
dotnet run --project Checks/Checks.csproj -c Release
dotnet publish Desktop/Desktop.csproj -c Release -r win-x64 --self-contained false -o Windows-x64
```

若需要免安装运行时版本：

```powershell
dotnet publish Desktop/Desktop.csproj -c Release -r win-x64 --self-contained true -o Standalone-x64
```

## 已验证与待验收

macOS 上完成 Windows 目标交叉编译：0 warnings / 0 errors；核心行为检查通过 418 项断言，包括中文 BOM 输入/无 BOM 输出、去重、顺序循环、100 轮随机词袋、跨轮防重复、冷却、失败不推进、原子替换、JSON 往返和路径限制。

Windows 发布文件生成完成。未在 Windows/CS2 实机运行，以下是必要验收步骤：

1. 配置有效路径和词库，准备消息，使用记事本确认 cfg 中只有一条 say 命令，中文正确。
2. 按住准备键不连发；快速再次按键显示冷却；不同词库独立更新。
3. 切换到无边框 CS2，确认悬浮框置顶、点击穿透、不会抢焦点；再手动 exec，确认中文消息。
4. 用另一应用占用某热键，确认应用配置报错且原热键仍可用。
5. 给不可写目录或锁定目标 cfg，确认报错且“下一条”未变化。
6. 暂停、恢复、隐藏悬浮框、最小化配置窗口、退出重开，确认行为符合上述说明。
