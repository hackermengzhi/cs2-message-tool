using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
namespace CS2MessageTool;
internal sealed class Overlay : Window {
    readonly TextBlock content=new() { Foreground=Brushes.White, TextWrapping=TextWrapping.Wrap, FontSize=14, Margin=new Thickness(16) };
    internal Overlay() {
        Title="CS2 消息预览"; WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
        Topmost=true; ShowInTaskbar=false; ShowActivated=false; Focusable=false; ResizeMode=ResizeMode.NoResize;
        SizeToContent=SizeToContent.Height; MaxHeight=700;
        Content=new Border { Background=new SolidColorBrush(Color.FromArgb(215,20,25,34)), CornerRadius=new CornerRadius(12), Child=content };
        SourceInitialized+=(_,_)=>Native.MakeOverlay(new WindowInteropHelper(this).Handle);
    }
    internal void Refresh(Settings s,IEnumerable<Channel> channels,string status) {
        Left=s.OverlayLeft; Top=s.OverlayTop; Width=s.OverlayWidth; Opacity=s.OverlayOpacity;
        content.Text="CS2 消息预览 · "+status+"\n\n"+string.Join("\n\n",channels.Select(c=>$"{c.Profile.Name} · {c.Profile.Hotkey} · {(c.Profile.Mode==PickMode.Random?"随机词袋":"顺序")}\n已写入：{c.Current}\n下一条：{c.Queue.Next}"))+"\n\n发送请在游戏内按单独绑定的 exec 键";
    }
}
