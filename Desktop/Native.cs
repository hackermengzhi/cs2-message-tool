using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
namespace CS2MessageTool;
internal static class Native {
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hwnd,int id);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW",SetLastError=true)] static extern int GetStyle(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)] static extern int SetStyle(nint hwnd,int index,int value);
    internal static void MakeOverlay(nint hwnd) {
        // GWL_EXSTYLE is 32 bits even in a 64-bit process.
        int style=GetStyle(hwnd,-20);
        Marshal.SetLastPInvokeError(0);
        int result=SetStyle(hwnd,-20,style|0x80000|0x20|0x08000000|0x80); // layered, transparent, noactivate, toolwindow
        if(result==0 && Marshal.GetLastPInvokeError()!=0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    internal static (uint Mod,uint Key) Parse(string text) {
        var parts=text.Split('+',StringSplitOptions.TrimEntries);
        uint mod=0;
        foreach(var part in parts[..^1]) {
            uint flag=part.ToLowerInvariant() switch { "ctrl"=>2, "alt"=>1, "shift"=>4, _=>throw new InvalidDataException("修饰键仅支持 Ctrl、Alt、Shift。") };
            if((mod&flag)!=0) throw new InvalidDataException("修饰键重复。"); mod|=flag;
        }
        if(!Enum.TryParse<Key>(parts[^1],true,out var key) || !Enum.IsDefined(key) || key==Key.None || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System || key==Key.F12)
            throw new InvalidDataException("无效热键（F12 为系统保留键）。");
        if(mod==0) throw new InvalidDataException("全局热键至少包含一个 Ctrl/Alt/Shift 修饰键，避免占用游戏单键。");
        return (mod|0x4000,(uint)KeyInterop.VirtualKeyFromKey(key)); // MOD_NOREPEAT
    }
}
internal sealed class Hotkeys : IDisposable {
    readonly nint handle; readonly HwndSource source;
    readonly Dictionary<int,Channel> registered=[];
    internal event Action<Channel>? Pressed;
    internal Hotkeys(Window window) {
        handle=new WindowInteropHelper(window).Handle;
        source=HwndSource.FromHwnd(handle)!; source.AddHook(Hook);
    }
    internal void Set(IEnumerable<Channel> channels) {
        Clear(); int id=1;
        try {
            foreach(var channel in channels) {
                var (mod,key)=Native.Parse(channel.Profile.Hotkey);
                if(!Native.RegisterHotKey(handle,id,mod,key)) throw new Win32Exception(Marshal.GetLastWin32Error(),"热键注册失败："+channel.Profile.Hotkey+"（可能被其他应用占用）");
                registered.Add(id++,channel);
            }
        } catch { Clear(); throw; }
    }
    nint Hook(nint hwnd,int msg,nint w,nint l,ref bool handled) {
        if(msg==0x0312 && registered.TryGetValue((int)w,out var c)) { handled=true; Pressed?.Invoke(c); } return 0;
    }
    void Clear() { foreach(var id in registered.Keys) Native.UnregisterHotKey(handle,id); registered.Clear(); }
    public void Dispose() { Clear(); source.RemoveHook(Hook); }
}
