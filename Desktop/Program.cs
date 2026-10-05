using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace CS2MessageTool;
internal static class Program {
    [STAThread] public static void Main() {
        using var mutex=new Mutex(true,@"Local\CS2MessageTool",out bool first);
        if(!first) { MessageBox.Show("工具已经运行，请使用已打开的配置窗口。"); return; }
        var app=new Application { ShutdownMode=ShutdownMode.OnMainWindowClose };
        app.Run(new ControlWindow());
    }
}
internal sealed class ControlWindow : Window {
    readonly string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CS2MessageTool");
    readonly TextBox editor=new() { AcceptsReturn=true,AcceptsTab=true, FontFamily=new FontFamily("Consolas"),FontSize=14, VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto };
    readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) };
    readonly Overlay overlay=new();
    Settings settings=new(); List<Channel> channels=[]; Hotkeys? hotkeys; bool paused;
    string ConfigPath=>Path.Combine(data,"settings.json");
    internal ControlWindow() {
        Title="CS2 消息工具 — 配置"; Width=850; Height=700;
        var layout=new DockPanel { Margin=new Thickness(16) };
        var description=new TextBlock { Text="1. 填写 CS2 cfg 文件夹路径。2. 修改词库与热键，保存应用。3. 按全局热键准备消息，再按游戏内 exec 键发送。\n配置为 JSON；词库为 UTF-8 txt，一行一条。相对词库路径基于数据文件夹。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12) };
        DockPanel.SetDock(description,Dock.Top); layout.Children.Add(description);
        var buttons=new WrapPanel(); DockPanel.SetDock(buttons,Dock.Bottom);
        AddButton(buttons,"保存并应用",()=>Apply());
        AddButton(buttons,"暂停 / 恢复热键",()=> { paused=!paused; SetStatus(paused?"已暂停，cfg 保留上次内容":"热键已恢复"); });
        AddButton(buttons,"显示 / 隐藏悬浮框",()=> { if(overlay.IsVisible) overlay.Hide(); else overlay.Show(); });
        AddButton(buttons,"打开词库文件夹",()=>Process.Start(new ProcessStartInfo(data) { UseShellExecute=true }));
        AddButton(buttons,"从磁盘重新载入配置",()=> { try { editor.Text=File.ReadAllText(ConfigPath,Storage.Utf8); SetStatus("已载入编辑器，请保存并应用"); } catch(Exception e) { SetStatus(e.Message); } });
        layout.Children.Add(buttons); DockPanel.SetDock(status,Dock.Bottom); layout.Children.Add(status); layout.Children.Add(editor); Content=layout;
        SourceInitialized+=(_,_)=> { hotkeys=new Hotkeys(this); hotkeys.Pressed+=Prepare; };
        Loaded+=(_,_)=>Initialize();
        Closed+=(_,_)=> { hotkeys?.Dispose(); overlay.Close(); };
    }
    static void AddButton(Panel p,string text,Action action) {
        var b=new Button { Content=text,Margin=new Thickness(0,4,8,4),Padding=new Thickness(10,7,10,7) };
        b.Click+=(_,_)=> { try { action(); } catch(Exception e) { MessageBox.Show(e.Message,"操作失败"); } }; p.Children.Add(b);
    }
    void Initialize() {
        try {
            Directory.CreateDirectory(data);
            foreach(var (file,text) in new[] { ("kill.txt","漂亮！\n这波配合不错\n继续加油"),("death.txt","下一回合见\n刚才没打好\n好枪"),("short.txt","收到\n谢谢\n加油") })
                if(!File.Exists(Path.Combine(data,file))) Storage.AtomicWrite(Path.Combine(data,file),text+"\n");
            if(!File.Exists(ConfigPath)) Storage.AtomicWrite(ConfigPath,Storage.Serialize(settings));
            editor.Text=File.ReadAllText(ConfigPath,Storage.Utf8);
            overlay.Refresh(settings,channels,"请配置路径"); overlay.Show(); Apply();
        } catch(Exception e) { SetStatus("初始化失败："+e.Message); }
    }
    void Apply() {
        try {
            var candidate=Storage.Parse(editor.Text); Storage.Validate(candidate);
            var seen=new HashSet<(uint,uint)>();
            var next=candidate.Profiles.Select(p=> {
                if(!seen.Add(Native.Parse(p.Hotkey))) throw new InvalidDataException("热键重复："+p.Hotkey);
                return new Channel(p,Storage.ReadLibrary(Path.IsPathFullyQualified(p.Library)?p.Library:Path.Combine(data,p.Library)));
            }).ToList();
            try {
                hotkeys!.Set(next);
                Storage.AtomicWrite(ConfigPath,Storage.Serialize(candidate));
            } catch(Exception error) {
                try { hotkeys!.Set(channels); }
                catch(Exception rollback) { paused=true; throw new InvalidOperationException(error.Message+"；旧热键恢复失败，已暂停："+rollback.Message); }
                throw;
            }
            settings=candidate; channels=next;
            SetStatus(paused?"配置已保存，热键仍暂停":"配置已保存，等待准备热键；尚未改写 cfg");
        } catch(Exception e) { SetStatus("应用失败："+e.Message); }
    }
    void Prepare(Channel c) {
        if(paused) { SetStatus("已暂停，未改写 cfg"); return; }
        try { SetStatus(c.Prepare(settings.CfgDirectory)); }
        catch(Exception e) { SetStatus(c.Profile.Name+"：写入失败，队列未推进："+e.Message); }
    }
    void SetStatus(string text) { status.Text=text; overlay.Refresh(settings,channels,text); }
}
