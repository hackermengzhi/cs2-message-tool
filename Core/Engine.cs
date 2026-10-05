using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CS2MessageTool;
public enum PickMode { Sequential, Random }
public sealed class Profile {
    public string Name { get; set; } = "kill";
    public string Hotkey { get; set; } = "Ctrl+Alt+F6";
    public string Library { get; set; } = "kill.txt";
    public string CfgFile { get; set; } = "trash_kill.cfg";
    public PickMode Mode { get; set; } = PickMode.Random;
    public int CooldownMs { get; set; } = 1000;
}
public sealed class Settings {
    public string CfgDirectory { get; set; } = "";
    public double OverlayLeft { get; set; } = 30;
    public double OverlayTop { get; set; } = 80;
    public double OverlayWidth { get; set; } = 460;
    public double OverlayOpacity { get; set; } = .88;
    public List<Profile> Profiles { get; set; } = [
        new() { Name="kill", Hotkey="Ctrl+Alt+F6", Library="kill.txt", CfgFile="trash_kill.cfg" },
        new() { Name="death", Hotkey="Ctrl+Alt+F7", Library="death.txt", CfgFile="trash_death.cfg" },
        new() { Name="short", Hotkey="Ctrl+Alt+F8", Library="short.txt", CfgFile="trash_short.cfg", Mode=PickMode.Sequential }
    ];
}
public static class Storage {
    public static readonly JsonSerializerOptions Json = new() { WriteIndented=true, Converters={new JsonStringEnumConverter()} };
    public static readonly UTF8Encoding Utf8 = new(false, true);
    public static Settings Parse(string json) => JsonSerializer.Deserialize<Settings>(json,Json) ?? throw new InvalidDataException("配置为空。");
    public static string Serialize(Settings settings) => JsonSerializer.Serialize(settings, Json);
    // Temp file is on the same volume; never truncate a live cfg before replacement.
    public static void AtomicWrite(string path, string content) {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                var bytes = Utf8.GetBytes(content); stream.Write(bytes); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp,path,null);
            else File.Move(temp,path);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string Say(string text) {
        // Reject syntax-bearing characters rather than relying on undocumented cfg escaping.
        if (string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl) || text.IndexOfAny(['"','\\',';']) >= 0 || text.Contains("//"))
            throw new InvalidDataException("消息含控制字符、引号、反斜杠、分号或 //，请移除。 ");
        if (Utf8.GetByteCount(text) > 120) throw new InvalidDataException("单条消息不能超过 120 个 UTF-8 字节（工具上限）。");
        return "say \"" + text + "\"\r\n";
    }
    public static List<string> ReadLibrary(string path) {
        var lines = File.ReadAllLines(path,Utf8).Select(s=>s.Trim()).Where(s=>s.Length>0).Distinct(StringComparer.Ordinal).ToList();
        if (lines.Count==0) throw new InvalidDataException("词库为空："+path);
        foreach (var line in lines) Say(line);
        return lines;
    }
    public static void Validate(Settings s) {
        if (string.IsNullOrWhiteSpace(s.CfgDirectory) || !Path.IsPathFullyQualified(s.CfgDirectory) || !Directory.Exists(s.CfgDirectory))
            throw new InvalidDataException("请填写已存在的 CS2 game\\csgo\\cfg 文件夹绝对路径。");
        if (!double.IsFinite(s.OverlayLeft) || !double.IsFinite(s.OverlayTop) || !double.IsFinite(s.OverlayWidth) || !double.IsFinite(s.OverlayOpacity) || s.OverlayWidth<280 || s.OverlayWidth>1200 || s.OverlayOpacity<.2 || s.OverlayOpacity>1)
            throw new InvalidDataException("悬浮框宽度 280–1200，透明度 0.2–1，坐标必须为有限数值。");
        if (s.Profiles is null || s.Profiles.Count is <1 or >12) throw new InvalidDataException("需要 1–12 个词库。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in s.Profiles) {
            if (p is null || string.IsNullOrWhiteSpace(p.Name) || !names.Add(p.Name)) throw new InvalidDataException("词库名称为空或重复。");
            if (p.CfgFile is null || !Regex.IsMatch(p.CfgFile,@"^[A-Za-z0-9_-]+\.cfg$") || !files.Add(p.CfgFile)) throw new InvalidDataException("cfg 名称须唯一，只允许字母、数字、下划线、横线及 .cfg 后缀。");
            if (string.IsNullOrWhiteSpace(p.Library) || string.IsNullOrWhiteSpace(p.Hotkey) || !Enum.IsDefined(p.Mode) || p.CooldownMs<0 || p.CooldownMs>600000) throw new InvalidDataException("词库、热键、模式或冷却时间无效。");
        }
    }
}
public sealed class MessageQueue {
    readonly List<string> words;
    readonly PickMode mode;
    readonly List<int> bag = [];
    int sequentialIndex;
    string? last;
    public string Next { get; private set; }
    public MessageQueue(List<string> words, PickMode mode) {
        if(words.Count==0) throw new ArgumentException("Empty library");
        this.words=words; this.mode=mode; Next=Choose();
    }
    string Choose() {
        if (mode==PickMode.Sequential) { var result=words[sequentialIndex]; sequentialIndex=(sequentialIndex+1)%words.Count; return result; }
        if (bag.Count==0) {
            bag.AddRange(Enumerable.Range(0,words.Count));
            for(int i=bag.Count-1;i>0;i--) { int j=Random.Shared.Next(i+1); (bag[i],bag[j])=(bag[j],bag[i]); }
            if (bag.Count>1 && words[bag[^1]]==last) (bag[0],bag[^1])=(bag[^1],bag[0]);
        }
        var index=bag[^1]; bag.RemoveAt(bag.Count-1); return words[index];
    }
    public void Commit() { last=Next; Next=Choose(); }
}
public sealed class Channel {
    public Profile Profile { get; }
    public MessageQueue Queue { get; }
    public string Current { get; private set; } = "尚未写入（磁盘可能保留旧内容）";
    long? writtenAt;
    public Channel(Profile p, List<string> words) { Profile=p; Queue=new(words,p.Mode); }
    public string Prepare(string directory) {
        long now=Stopwatch.GetTimestamp();
        if(writtenAt is long t && Stopwatch.GetElapsedTime(t,now).TotalMilliseconds<Profile.CooldownMs) return Profile.Name+"：冷却中，未改写";
        var message=Queue.Next;
        Storage.AtomicWrite(Path.Combine(directory,Profile.CfgFile),Storage.Say(message));
        // Advance only after durable replacement succeeded.
        Current=message; writtenAt=Stopwatch.GetTimestamp(); Queue.Commit();
        return Profile.Name+"：已写入 "+Profile.CfgFile+"，等待游戏内手动 exec";
    }
}
