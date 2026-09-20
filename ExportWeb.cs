using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using D = System.Collections.Generic.Dictionary<string,object>;

namespace HgssStrategyReader {
  // Compile every possible browser screen using the actual native reader. The
  // hosted site needs only these verified screens, not a second game simulator.
  static class ExportWeb {
    static readonly Encoding Utf8=new UTF8Encoding(false);
    static string Hash(byte[] bytes) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
    static object[] Steps(D view) { return Json.Items(view["steps"]).Where(x=>Json.Text(Json.Obj(x),"kind")!="entry").ToArray(); }
    static int Emit(StrategyEngine engine,Cursor cursor,List<D> nodes) {
      var block=engine.Project(cursor,CancellationToken.None);engine.Current=cursor;engine.Block=block;var view=engine.View();
      int id=nodes.Count;nodes.Add(null);var choices=new List<D>();
      if(!block.Terminal) {
        var waiting=block.Waiting;var groups=engine.Game.Responses(waiting.Session,CancellationToken.None).ToDictionary(g=>g.Key);
        foreach(var choice in block.Choices) {
          string key=Json.Text(choice,"key");var group=groups[key];
          int next=Emit(engine,new Cursor{Session=waiting.Session.With(group.Next),Policy=waiting.Policy.Branches[key]},nodes);
          choices.Add(Json.Map("cues",choice["cues"],"count",choice["count"],"search",choice["search"],"next",next));
        }
      }
      nodes[id]=Json.Map("terminal",block.Terminal,"remaining",view["remaining"],"startingAdvances",view["startingAdvances"],"steps",Steps(view),"choices",choices);
      return id;
    }
    static void Verify(StrategyEngine engine,List<D> nodes,int id,HashSet<long> origins) {
      var expected=nodes[id];var actual=engine.View();
      if(Json.Encode(expected["steps"])!=Json.Encode(Steps(actual)) || Json.Encode(expected["startingAdvances"])!=Json.Encode(actual["startingAdvances"]) || Json.Int(expected,"remaining")!=Json.Int(actual,"remaining") || Json.Bool(expected,"terminal")!=Json.Bool(actual,"terminal"))throw new Exception("Web screen differs from native reader");
      var choices=Json.Items(expected["choices"]).Select(Json.Obj).ToArray();var native=Json.Items(actual["choices"]).Select(Json.Obj).ToArray();
      if(choices.Length!=native.Length)throw new Exception("Response choices differ");
      if(Json.Bool(expected,"terminal"))foreach(var value in Json.Items(expected["startingAdvances"]))if(!origins.Add(Convert.ToInt64(value)))throw new Exception("Duplicate original start");
      for(int i=0;i<choices.Length;i++) {
        if(Json.Encode(choices[i]["cues"])!=Json.Encode(native[i]["cues"]) || Json.Int(choices[i],"count")!=Json.Int(native[i],"count"))throw new Exception("Response cue or cohort differs");
        engine.Choose(Json.Text(native[i],"key"),engine.Revision);Verify(engine,nodes,Json.Int(choices[i],"next"),origins);engine.Back();
      }
    }
    static int Main(string[] args) {
      string appRoot=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(Path.Combine(output,"data"));Directory.CreateDirectory(Path.Combine(output,"emotes"));
      var game=new GameModel();var bands=new List<D>();var reports=new List<D>();var sources=Json.Items(Json.Decode(File.ReadAllText(Path.Combine(appRoot,"data","sources.json")))).Select(Json.Obj).ToDictionary(d=>Json.Text(d,"name"));
      string[] names={"Q2","Q3","Q4","Full"},ranges={"17–32 HP","33–48 HP","49–64 HP","65 HP"};
      for(int i=0;i<names.Length;i++) {
        string name="raikou_"+names[i].ToLowerInvariant()+"_opt.json";byte[] bytes=File.ReadAllBytes(Path.Combine(appRoot,"data",name));
        if(Hash(bytes)!=Json.Text(sources[name],"sha256"))throw new Exception("Original strategy hash mismatch: "+name);
        var file=Json.Parse(Utf8.GetString(bytes));var engine=new StrategyEngine(game);engine.Load(file,name,CancellationToken.None,null);var initial=engine.View();
        var nodes=new List<D>();Emit(engine,engine.Root,nodes);
        var check=new StrategyEngine(game);check.Load(file,name,CancellationToken.None,null);var origins=new HashSet<long>();Verify(check,nodes,0,origins);
        if(!origins.OrderBy(n=>n).SequenceEqual(Enumerable.Range(3146,214).Select(n=>(long)n)))throw new Exception("Incomplete web strategy");
        var data=Json.Map("format","raikou-web-v1","band",names[i],"seed",initial["seed"],"first",initial["first"],"last",initial["last"],"target",initial["target"],"details",initial["details"],"nodes",nodes);
        byte[] encoded=Utf8.GetBytes(Json.Encode(data));string hash=Hash(encoded),filename=names[i].ToLowerInvariant()+"."+hash.Substring(0,12)+".json";
        File.WriteAllBytes(Path.Combine(output,"data",filename),encoded);
        bands.Add(Json.Map("id",names[i],"range",ranges[i],"url","data/"+filename,"sha256",hash));
        reports.Add(Json.Map("band",names[i],"nodes",nodes.Count,"starts",origins.Count,"bytes",encoded.Length,"sourceSha256",Hash(bytes),"nativeParity",true));
      }
      var emotes=new D();var css=new StringBuilder("/* Original static sprite frames; crop only the transparent canvas in CSS. */\n");
      foreach(var pair in game.Emotes) {
        var data=Json.Obj(pair.Value);string url=Convert.ToString(Json.Items(data["frames"]).First());byte[] bytes=Convert.FromBase64String(url.Substring(url.IndexOf(',')+1));
        File.WriteAllBytes(Path.Combine(output,"emotes",pair.Key+".png"),bytes);
        using(var stream=new MemoryStream(bytes))using(var image=new Bitmap(stream)) {
          int left=image.Width,top=image.Height,right=-1,bottom=-1;
          for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)if(image.GetPixel(x,y).A!=0){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
          if(right<0)throw new Exception("Empty emote");
          Func<double,string> number=n=>n.ToString(System.Globalization.CultureInfo.InvariantCulture);
          css.Append(".emote[data-emote=\"").Append(pair.Key).Append("\"]{width:").Append(number((right-left+1)*1.5)).Append("px;height:").Append(number((bottom-top+1)*1.5)).Append("px}.emote[data-emote=\"").Append(pair.Key).Append("\"] img{width:").Append(number(image.Width*1.5)).Append("px;height:").Append(number(image.Height*1.5)).Append("px;left:").Append(number(-left*1.5)).Append("px;top:").Append(number(-top*1.5)).Append("px}\n");
        }
        emotes.Add(pair.Key,Json.Map("src","emotes/"+pair.Key+".png","label",data["label"]));
      }
      File.WriteAllText(Path.Combine(output,"emotes.css"),css.ToString(),Utf8);
      File.WriteAllText(Path.Combine(output,"manifest.json"),Json.Encode(Json.Map("format","raikou-web-v1","model",BuildInfo.ModelId,"bands",bands,"emotes",emotes)),Utf8);
      var report=Json.Map("passed",true,"starts",856,"bands",reports);File.WriteAllText(args[2],Json.Encode(report),Utf8);Console.WriteLine(Json.Encode(report));return 0;
    }
  }
}
