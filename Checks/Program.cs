using CS2MessageTool;
using System.Text;
int checks=0;
void Assert(bool pass,string label) { if(!pass) throw new Exception(label); checks++; }
void Reject(Action action,string label) { try { action(); } catch(InvalidDataException) { checks++; return; } throw new Exception(label); }
var temp=Path.Combine(Path.GetTempPath(),"cs2msg-check-"+Guid.NewGuid()); Directory.CreateDirectory(temp);
try {
    foreach(var text in new[] { "a\"; quit", "a\\b", "a;b", "a\nb", "a//b", new string('中',41) }) Reject(()=>Storage.Say(text),"Reject cfg syntax");
    Assert(Storage.Say("好枪") == "say \"好枪\"\r\n","Chinese say");
    File.WriteAllText(Path.Combine(temp,"words.txt"),"好枪\n\n好枪\n收到\n",new UTF8Encoding(true));
    var words=Storage.ReadLibrary(Path.Combine(temp,"words.txt")); Assert(words.Count==2,"BOM + dedup");
    var seq=new MessageQueue(words,PickMode.Sequential); Assert(seq.Next=="好枪","Sequence preview"); seq.Commit(); Assert(seq.Next=="收到","Sequence step"); seq.Commit(); Assert(seq.Next=="好枪","Sequence wrap");
    var random=new MessageQueue(["a","b","c"],PickMode.Random); string? last=null;
    for(int cycle=0;cycle<100;cycle++) { var bag=new HashSet<string>(); for(int i=0;i<3;i++) { Assert(last!=random.Next,"Random adjacent repeat"); bag.Add(random.Next); last=random.Next; random.Commit(); } Assert(bag.Count==3,"Random bag coverage"); }
    var p=new Profile { CfgFile="trash.cfg",CooldownMs=600000 }; var channel=new Channel(p,words);
    channel.Prepare(temp); var bytes=File.ReadAllBytes(Path.Combine(temp,"trash.cfg")); Assert(!(bytes[0]==0xef && bytes[1]==0xbb),"UTF8 no BOM"); Assert(File.ReadAllText(Path.Combine(temp,"trash.cfg")).Contains(channel.Current),"Written current");
    var next=channel.Queue.Next; channel.Prepare(temp); Assert(channel.Queue.Next==next,"Cooldown doesn't advance");
    var failed=new Channel(p,words); var preview=failed.Queue.Next;
    try { failed.Prepare(Path.Combine(temp,"missing")); throw new Exception("Expected IO failure"); } catch(DirectoryNotFoundException) { Assert(failed.Queue.Next==preview,"Failure doesn't advance"); }
    Storage.AtomicWrite(Path.Combine(temp,"trash.cfg"),Storage.Say("替换成功")); Assert(File.ReadAllText(Path.Combine(temp,"trash.cfg")).Contains("替换成功"),"Atomic replacement");
    var s=new Settings { CfgDirectory=temp }; Storage.Validate(s); Assert(Storage.Parse(Storage.Serialize(s)).Profiles.Count==3,"Config roundtrip");
    s.Profiles[0].CfgFile="../autoexec.cfg"; Reject(()=>Storage.Validate(s),"Prevent path escape");
    Console.WriteLine($"Passed {checks} assertions.");
} finally { Directory.Delete(temp,true); }
