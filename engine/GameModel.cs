using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using D = System.Collections.Generic.Dictionary<string,object>;

namespace HgssStrategyReader {
  sealed class Mon {
    public int Species,Form,Ability,HeldItem,HP,MaxHP,Mood,Friendship; public uint Status; public string Nickname,SpeciesName;
    public Mon Copy(){return (Mon)MemberwiseClone();}
  }
  sealed class Context {
    public Mon Mon; public int Map,Tile,HiddenItems,PlayerX,PlayerZ,PlayerFacing,FollowerFacing,TalkFacing,TextSpeed; public int[] Flags; public string Trainer;
    public Context WithMon(Mon m){var c=(Context)MemberwiseClone();c.Mon=m;return c;}
    public static Context Parse(D d) {
      var m=Json.RequireObject(Json.Require(d,"mon"));var player=Json.RequireObject(Json.Require(d,"player"));var follower=Json.RequireObject(Json.Require(d,"follower"));
      Func<D,string,int,int,int> n=(a,k,lo,hi)=>(int)Json.Integer(Json.Require(a,k),lo,hi,k);
      var mon=new Mon{Species=n(m,"species",1,493),Form=n(m,"form",0,255),Ability=n(m,"ability",0,255),HeldItem=n(m,"heldItem",0,65535),HP=n(m,"hp",1,65535),MaxHP=n(m,"maxHP",1,65535),Mood=n(m,"mood",-127,127),Friendship=n(m,"friendship",0,255),Status=(uint)Json.Integer(Json.Require(m,"status"),0,uint.MaxValue,"status"),Nickname=Json.Text(m,"nickname"),SpeciesName=Json.Text(m,"speciesName")};
      if(mon.HP>mon.MaxHP)throw new InvalidDataException("HP exceeds Max HP.");
      int facing=n(player,"facing",0,3);
      return new Context{Mon=mon,Map=n(d,"map",0,65535),Tile=n(d,"tile",0,65535),HiddenItems=n(d,"hiddenItems",0,65535),PlayerX=n(player,"x",0,31),PlayerZ=n(player,"z",0,31),PlayerFacing=facing,FollowerFacing=n(follower,"facing",0,3),TalkFacing=Json.Get(d,"talkFacing")==null?facing:n(d,"talkFacing",0,3),TextSpeed=Json.Get(d,"textSpeed")==null?-1:n(d,"textSpeed",0,15),Trainer=Json.Text(d,"trainer"),Flags=Json.Array(Json.Require(d,"flags")).Select(v=>(int)Json.Integer(v,0,255,"flag byte")).ToArray()};
    }
  }
  sealed class TurnPattern {
    public string Scope; public int[] Directions;
    public static int[] ParseText(string text){
      var words=Regex.Split(text.Replace("->"," ").Trim().ToLowerInvariant(),@"[\s,;→>]+" );
      var result=words.Where(w=>w.Length>0).Select(w=>w=="u"||w=="up"||w=="n"||w=="north"?0:w=="d"||w=="down"||w=="s"||w=="south"?1:w=="l"||w=="left"||w=="w"||w=="west"?2:w=="r"||w=="right"||w=="e"||w=="east"?3:-1).ToArray();ValidateDirections(result);return result;
    }
    public static void ValidateDirections(int[] dirs){if(dirs.Length<2||dirs.Length>64||dirs.Any(d=>d<0||d>3))throw new InvalidDataException("Enter 2–64 directions: Up, Down, Left, Right (or U, D, L, R).");for(int i=0;i<dirs.Length;i++)if(dirs[i]==dirs[(i+1)%dirs.Length])throw new InvalidDataException(i==dirs.Length-1?"Last and first directions match; repeating would walk.":"Adjacent directions match; that would walk.");}
    public static TurnPattern Parse(object value){if(value==null)return null;var p=Json.RequireObject(value);string scope=Json.Text(p,"scope");if(scope!="all"&&scope!="finish")throw new InvalidDataException("Invalid turn sequence scope.");var dirs=Json.Array(Json.Require(p,"directions")).Select(v=>(int)Json.Integer(v,0,3,"direction")).ToArray();ValidateDirections(dirs);return new TurnPattern{Scope=scope,Directions=dirs};}
    public void ValidateBlock(int[] dirs,int facing){int phase=0;foreach(int d in dirs){if(phase==0&&Directions[0]==facing)phase=1;if(d!=Directions[phase])throw new InvalidDataException("Strategy does not follow its repeated turn sequence.");phase=(phase+1)%Directions.Length;facing=d;}}
  }
  sealed class TurnConfig {
    public int Facing,LastDirection,Counter,Inhibit,LeadLevel,EncounterRate,CalculatedRate; public int? SecondChance; public string Ability,Flute,Radio; public bool Item;
    public TurnConfig Copy(){return (TurnConfig)MemberwiseClone();}
  }
  sealed class TurnState {
    public uint Seed; public long Cycle; public int Facing,LastDirection,Counter,Inhibit; public long Turn;
    public TurnState Copy(){return (TurnState)MemberwiseClone();}
  }
  sealed class TimingConfig {
    public double TurnsPerSecond=4,InputDelay=0,QuestionSeconds=1,BonkSeconds=0.25; public bool WallAbove=true;
    public static TimingConfig Parse(D d) {
      var c=new TimingConfig();if(d==null)return c;
      Func<string,double,double> v=(key,fallback)=>{object o=Json.Get(d,key);double n=o==null?fallback:Convert.ToDouble(o,CultureInfo.InvariantCulture);if(double.IsInfinity(n)||double.IsNaN(n)||n<0)throw new InvalidDataException("Invalid timing setting: "+key);return n;};
      c.TurnsPerSecond=v("turnsPerSecond",4);c.InputDelay=v("inputDelay",0);c.QuestionSeconds=v("questionSeconds",1);c.BonkSeconds=v("bonkSeconds",0.25);if(c.TurnsPerSecond==0)throw new InvalidDataException("Turning speed must be positive.");if(d.ContainsKey("wallAbove"))c.WallAbove=Json.Bool(d,"wallAbove");return c;
    }
  }
  sealed class Candidate {
    public long Origin,Cycle; public uint Seed; public Context Context;
    public Candidate Copy(){return (Candidate)MemberwiseClone();}
  }
  sealed class Session {
    public ResponseCatalog Catalog; public uint Seed; public long First,Last,Target;public Context Context;public TurnConfig Config;public TimingConfig Finish;public string Answer,Entry;public int TalkFacing,ArrivalLastDirection; public bool Arrival;public List<Candidate> Candidates;
    public Session With(List<Candidate> cs,TurnConfig config=null){var s=(Session)MemberwiseClone();s.Candidates=cs;if(config!=null)s.Config=config;return s;}
  }
  sealed class Rule { public int Index,Id,Chance;public bool Eligible; }
  sealed class Cue { public int Emote;public string Text; public D Data(){return Json.Map("emote",Emote,"text",Text);} }
  sealed class Completion { public Context Context;public List<Cue> Cues=new List<Cue>();public List<string> Texts=new List<string>(); }
  sealed class Timing { public double? Seconds;public int Ticks,MovementTicks,EmoteTicks,TextTicks,Inputs,Pages,Scrolls;public double QuestionSeconds;public List<int> Chain=new List<int>(); }
  sealed class ResponseGroup {
    public string Key,Label;public List<Cue> Cues;public List<Candidate> Next=new List<Candidate>();public List<Timing> Timings=new List<Timing>();
  }
  sealed class GameModel {
    public readonly D Data,Timings,Emotes; readonly D species,actions,texts,personalTypes,movement; readonly int[][] entries;readonly int[] indexes;public readonly int[] Tiles,Levels;
    public static readonly string[] Names={"Up","Down","Left","Right"};
    static readonly Regex Variable=new Regex(@"\{STRVAR_1 \d+, (\d+), \d+\}",RegexOptions.Compiled);
    static readonly Regex Controls=new Regex(@"\{[^}]+\}",RegexOptions.Compiled),Breaks=new Regex(@"\\[nrf]",RegexOptions.Compiled),Spaces=new Regex(@"\s+",RegexOptions.Compiled);
    public static string ReadResource(string name){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){if(s==null)throw new InvalidDataException("Missing embedded data: "+name);using(var r=new StreamReader(s))return r.ReadToEnd();}}
    public GameModel():this(Json.Parse(ReadResource("game-data.json")),Json.Parse(ReadResource("movement-timings.json")),Json.Parse(ReadResource("emotes.json"))){}
    public GameModel(D data,D timings,D emotes) {
      Data=data;Timings=timings;Emotes=emotes;species=Json.RequireObject(Json.Require(data,"speciesData"));actions=Json.RequireObject(Json.Require(data,"actions"));texts=Json.RequireObject(Json.Require(data,"texts"));personalTypes=Json.RequireObject(Json.Require(data,"personalTypes"));movement=Json.RequireObject(Json.Require(timings,"movement"));
      var rules=Json.Array(Json.Require(data,"entries")).Select(Json.RequireObject).ToArray();entries=rules.Select(e=>Json.Array(Json.Require(e,"bytes")).Select(Convert.ToInt32).ToArray()).ToArray();indexes=rules.Select(e=>Json.Int(e,"index")).ToArray();
      Tiles=Json.Array(Json.Require(data,"tiles")).Select(Convert.ToInt32).ToArray();var encounter=Json.RequireObject(Json.Require(data,"turnEncounter"));if(Json.Int(encounter,"rate")!=10)throw new InvalidDataException("Unsupported encounter table.");Levels=Json.Array(Json.Require(encounter,"levels")).Select(Convert.ToInt32).ToArray();
    }
    public static uint Next(uint seed){return unchecked(seed*0x41c64e6dU+0x6073U);}
    public static uint Jump(uint seed,long advances){if(advances<0)throw new InvalidDataException("Negative advance.");uint a=0x41c64e6d,c=0x6073;unchecked{while(advances>0){if((advances&1)!=0)seed=seed*a+c;c*=a+1;a*=a;advances/=2;}}return seed;}
    public static string Hex(uint seed){return "0x"+seed.ToString("X8",CultureInfo.InvariantCulture);}
    public static string StatusKey(uint status){return (status&128)!=0?"toxic":(status&8)!=0?"poison":(status&7)!=0?"sleep":(status&16)!=0?"burn":(status&32)!=0?"freeze":(status&64)!=0?"paralysis":"healthy";}
    public static bool FriendshipMatches(int group,int v){var values=new[]{true,v==255,v>=200&&v<255,v>=150&&v<200,v>=90&&v<150,v>=60&&v<90,v>=30&&v<60,v>=1&&v<30,v==0,v>=90,v<60};return group>=0&&group<values.Length&&values[group];}
    public static bool MoodMatches(int group,int v){var values=new[]{true,v==127,v>=100&&v<127,v>=50&&v<100,v>=30&&v<50,v> -30&&v<30,v> -50&&v<=-30,v> -127&&v<=-50,v== -127,v>=0,v<=-1};return group>=0&&group<values.Length&&values[group];}
    public static bool SpeciesMatches(int group,int v){if(group==0)return true;if(group<=249)return group==v;var values=new[]{v<=19,v<=130,v>=140&&v<=149,v>=160,v>=220};return group>=250&&group<255&&values[group-250];}
    D Species(int id){var s=Json.Obj(Json.Get(species,id.ToString()));if(s==null)throw new InvalidDataException("Unsupported Pokémon species.");return s;}
    public int[] Types(Mon mon){var info=Species(mon.Species);if(mon.Species==493&&mon.Ability==121){int t=mon.HeldItem>=298&&mon.HeldItem<=313?mon.HeldItem-296:1;return new[]{t,t};}string[] forms=null;switch(mon.Species){case 386:forms=new[]{"DEOXYS_ATK","DEOXYS_DEF","DEOXYS_SPD"};break;case 413:forms=new[]{"WORMADAM_SANDY","WORMADAM_TRASH"};break;case 487:forms=new[]{"GIRATINA_ORIGIN"};break;case 492:forms=new[]{"SHAYMIN_SKY"};break;case 479:forms=new[]{"ROTOM_HEAT","ROTOM_WASH","ROTOM_FROST","ROTOM_FAN","ROTOM_MOW"};break;}return Json.Array(forms!=null&&mon.Form>=1&&mon.Form<=forms.Length?Json.Require(personalTypes,forms[mon.Form-1]):Json.Require(info,"types")).Select(Convert.ToInt32).ToArray();}
    static int U16(int[] b,int i){return b[i]|(b[i+1]<<8);}
    public Rule[] Compile(Context ctx){
      var m=ctx.Mon;if(m.Species==441)throw new InvalidDataException("Chatot cry RNG depends on runtime audio state and is not supported.");
      int percent=m.HP*100/m.MaxHP,hp=percent==100?1:percent>=75?2:percent>=50?3:percent>=25?4:5;
      int status=(m.Status&0x88)!=0?5:(m.Status&7)!=0?8:(m.Status&16)!=0?2:(m.Status&32)!=0?3:(m.Status&64)!=0?4:1;
      int dir=new[]{3,4,2,1}[ctx.FollowerFacing],speciesGroup=Json.Int(Species(m.Species),"group");var types=Types(m);var result=new Rule[entries.Length];
      for(int i=0;i<entries.Length;i++){var b=entries[i];if(b.Length!=20||b[3]!=0||(b[4]>>5)!=0||b[5]!=0||b[7]!=0||b[8]!=0||(b[9]&31)!=0||(b[10]&63)!=0||b[13]!=0||b[15]!=0||(b[16]&31)!=0||(b[2]&31)!=0)throw new InvalidDataException("Unsupported embedded response condition.");
        int requiredStatus=b[2]>>5,hidden=b[16]>>5,flag=U16(b,18);
        bool eligible=(b[0]==0||b[0]==hp)&&(requiredStatus==0||requiredStatus==status||requiredStatus==7&&status!=1)&&MoodMatches(b[1]&15,m.Mood)&&FriendshipMatches(b[1]>>4,m.Friendship)&&((b[4]&31)==0||types.Contains(b[4]&31))&&SpeciesMatches(b[6],speciesGroup)&&((b[9]>>5)==0||(b[9]>>5)==dir)&&(U16(b,12)==0||U16(b,12)==ctx.Map+1)&&(U16(b,14)==0||U16(b,14)==ctx.Tile)&&(hidden==0||hidden==ctx.HiddenItems||hidden==4&&ctx.HiddenItems>=4)&&(flag==0||flag/8<ctx.Flags.Length&&(ctx.Flags[flag/8]&(1<<(flag&7)))!=0);
        result[i]=new Rule{Index=indexes[i],Id=U16(b,10)>>6,Chance=b[17],Eligible=eligible};
      }return result;
    }
    public int Predict(Candidate before,Rule[] rules,out Candidate after){after=before.Copy();foreach(var r in rules){after.Seed=Next(after.Seed);after.Cycle++;if((after.Seed>>16)%100<r.Chance&&r.Eligible)return r.Id;}throw new InvalidDataException("No follower response matched.");}
    string RawText(int id,Context ctx,bool timing){string raw=Convert.ToString(Json.Get(texts,id.ToString()))??(timing?"":"[Message "+id+"]");var s=Species(ctx.Mon.Species);string[] values={string.IsNullOrEmpty(ctx.Mon.Nickname)?Json.Text(s,"label"):ctx.Mon.Nickname,Json.Text(s,"name"),ctx.Trainer,"Burned Tower","Item "+ctx.Mon.HeldItem};return Controls.Replace(Variable.Replace(raw,m=>{int n=int.Parse(m.Groups[1].Value);return n<values.Length?values[n]:"";}),"");}
    public string RenderText(int id,Context ctx){return Spaces.Replace(Breaks.Replace(RawText(id,ctx,false)," ")," ").Trim();}
    public Completion Complete(int id,Context ctx,string answer,int depth=0){
      var a=Json.Obj(Json.Get(actions,id.ToString()));if(a==null||depth>5)throw new InvalidDataException("Unsupported response continuation.");if(Json.Truth(Json.Get(a,"leaf")))throw new InvalidDataException("Shiny Leaf state changes are unsupported.");
      var mon=ctx.Mon.Copy();mon.Mood=Math.Max(-127,Math.Min(127,mon.Mood+Json.Int(a,"mood")));mon.Friendship=Math.Max(0,Math.Min(255,mon.Friendship+Json.Int(a,"friendship")));
      var done=new Completion{Context=ctx.WithMon(mon)};
      foreach(var item in Json.Items(Json.Get(a,"steps"))){var step=Json.RequireObject(item);int message=Json.Int(step,"message"),emote=Json.Int(step,"emotion");if(message>=0)done.Texts.Add(RenderText(message,ctx));if(emote!=0||message>=0)done.Cues.Add(new Cue{Emote=emote,Text=message>=0?RenderText(message,ctx):""});}
      if(done.Texts.Count==0)done.Texts.Add("(Animation / cry only)");if(Json.Truth(Json.Get(a,"accessory")))done.Texts.Add("(Accessory interaction)");
      if(Json.Truth(Json.Get(a,"question"))){string label="Answer "+(answer=="yes"?"Yes":"No");done.Texts.Add("["+label+"]");done.Cues.Add(new Cue{Emote=0,Text=label});int next=Json.Int(a,answer);if(next!=0){var tail=Complete(next,done.Context,answer,depth+1);done.Context=tail.Context;done.Cues.AddRange(tail.Cues);done.Texts.AddRange(tail.Texts);}}
      return done;
    }
    public static int[] Printer(string raw){int calls=1,inputs=1,pages=0,scrolls=0;for(int i=0;i<raw.Length;i++){if(raw[i]=='\\'&&i+1<raw.Length&&"nrf".Contains(raw[i+1])){char c=raw[++i];if(c=='r'){calls+=2;inputs++;pages++;}if(c=='f'){calls+=7;inputs++;scrolls++;}}else calls++;}return new[]{(calls+1)/2,inputs,pages,scrolls};}
    public Timing Estimate(int id,Context ctx,string answer,TimingConfig cfg){
      cfg=cfg??new TimingConfig();var t=new Timing{Ticks=6};int action=id;
      for(int depth=0;action!=0&&depth<6;depth++){var a=Json.RequireObject(Json.Get(actions,action.ToString()));if(Json.Truth(Json.Get(a,"accessory"))||Json.Truth(Json.Get(a,"leaf")))return t;t.Chain.Add(action);
        foreach(var item in Json.Items(Json.Get(a,"steps"))){var step=Json.RequireObject(item);int motion=Json.Int(step,"motion"),move=motion==0?0:Json.Int(Json.RequireObject(Json.Get(movement,motion.ToString())),"executionTicks"),emote=Json.Int(step,"emotion")!=0?36:0;t.MovementTicks+=move;t.EmoteTicks+=emote;t.Ticks+=move+emote+1+Json.Int(step,"wait");int message=Json.Int(step,"message");if(message>=0){var p=Printer(RawText(message,ctx,true));t.TextTicks+=p[0];t.Ticks+=p[0]+1;t.Inputs+=p[1];t.Pages+=p[2];t.Scrolls+=p[3];}}
        if(Json.Truth(Json.Get(a,"question"))){t.QuestionSeconds+=cfg.QuestionSeconds;t.Inputs++;action=Json.Int(a,answer);}else action=0;
      }t.Ticks+=ctx.TextSpeed==2?0:1;t.Seconds=t.Ticks/30.0+t.Inputs*cfg.InputDelay+t.QuestionSeconds;return t;
    }
    public static string CueKey(IEnumerable<Cue> cues){return "["+string.Join(",",cues.Select(c=>"{\"emote\":"+c.Emote+",\"text\":"+Json.Quote(c.Text)+"}"))+"]";}
    public List<ResponseGroup> Responses(Session s,CancellationToken token){var groups=new Dictionary<int,ResponseGroup>();var catalog=s.Catalog??new ResponseCatalog(this,s.Context,s.Answer,s.Finish);foreach(var c in s.Candidates){token.ThrowIfCancellationRequested();Candidate next;int id=Predict(c,catalog.Rules(c.Context),out next);var entry=catalog[id];next.Context=entry.After(c.Context);ResponseGroup group;if(!groups.TryGetValue(entry.CueId,out group))groups[entry.CueId]=group=new ResponseGroup{Key=entry.Key,Label=entry.Label,Cues=entry.Cues};group.Next.Add(next);group.Timings.Add(entry.Timing);}return groups.Values.OrderBy(g=>g.Label,StringComparer.Create(CultureInfo.GetCultureInfo("en-US"),false)).ToList();}
    public TurnConfig Configure(D turns,int facing){
      turns=turns??new D();Func<string,int,int,int,int> get=(k,defaultValue,min,max)=>(int)Json.InputInteger(Json.Get(turns,k)??defaultValue,min,max,k);
      var c=new TurnConfig{Facing=facing,LastDirection=get("lastDirection",0,0,3),Counter=get("counter",0,0,65535),Inhibit=get("inhibit",3,0,65535),Ability=Json.Text(turns,"ability"),Flute=Json.Text(turns,"flute"),Radio=Json.Text(turns,"radio"),Item=Json.Truth(Json.Get(turns,"item"))};
      if(!turns.ContainsKey("ability"))c.Ability="neutral";if(!turns.ContainsKey("flute"))c.Flute="none";if(!turns.ContainsKey("radio"))c.Radio="none";
      if(!new[]{"neutral","slot","level","suppress","double","half"}.Contains(c.Ability)||!new[]{"none","black","white"}.Contains(c.Flute)||!new[]{"none","march","lullaby"}.Contains(c.Radio))throw new InvalidDataException("Invalid encounter setting.");
      if(c.Ability=="suppress")c.LeadLevel=get("leadLevel",20,1,100);else {try{c.LeadLevel=get("leadLevel",20,1,100);}catch(InvalidDataException){c.LeadLevel=20;}}
      int rate=c.Ability=="double"?20:c.Ability=="half"?5:10;if(c.Flute=="black")rate/=2;else if(c.Flute=="white")rate+=rate/2;if(c.Item)rate=rate*2/3;c.CalculatedRate=Math.Min(100,rate);
      var second=Json.Get(turns,"secondChance");c.SecondChance=second==null||Convert.ToString(second).Trim()==""?(int?)null:(int)Json.InputInteger(second,0,100,"secondChance");c.EncounterRate=c.SecondChance??c.CalculatedRate;return c;
    }
    public static TurnState Initial(TurnConfig c,uint seed,long cycle){return new TurnState{Seed=seed,Cycle=cycle,Facing=c.Facing,LastDirection=c.LastDirection,Counter=c.Counter,Inhibit=c.Inhibit};}
    public TurnState Move(TurnState before,TurnConfig c,int direction,string kind="turn",D detail=null) {
      if(direction<0||direction>3||!new[]{"turn","walk","run"}.Contains(kind))throw new InvalidDataException("Invalid movement.");if(kind=="turn"&&direction==before.Facing)throw new InvalidDataException("This input walks instead of turning.");var s=before.Copy();s.Turn++;s.Facing=direction;s.Inhibit=Math.Min(65535,s.Inhibit+1);
      var draws=detail==null?null:new List<D>();string outcome="Initial encounter checks inhibited";int? firstRate=null;
      Func<string,int,int> draw=(label,mod)=>{s.Seed=Next(s.Seed);s.Cycle++;int value=(int)((s.Seed>>16)%mod);if(draws!=null)draws.Add(Json.Map("cycle",s.Cycle,"label",label,"value",value,"modulus",mod,"seed",s.Seed));return value;};
      if(s.Inhibit>3){if(direction==(s.LastDirection^1))s.Counter=Math.Min(65535,s.Counter+1);s.LastDirection=direction;int boost=s.Counter>=4?60:s.Counter==3?40:s.Counter==2?30:0;int first=Math.Min(100,((kind=="run"?40:20)+boost+(c.Radio=="march"?25:c.Radio=="lullaby"?-25:0))&255);firstRate=first;outcome="First roll failed";
        if(draw("First encounter roll",100)<first){outcome="Second roll failed";if(draw("Second encounter roll",100)<c.EncounterRate){
          if(c.Ability=="slot")draw("Static / Magnet Pull check",2);int roll=draw("Land encounter slot",100),slot=0;var thresholds=new[]{20,40,50,60,70,80,85,90,94,98,99,100};while(roll>=thresholds[slot])slot++;if(c.Ability=="level")draw("Level ability check",2);outcome="Repel blocked encounter";if(c.Ability=="suppress"&&c.LeadLevel>5&&Levels[slot]<=c.LeadLevel-5&&draw("Intimidate / Keen Eye check",2)==0)outcome="Ability blocked encounter";
        }}
      }
      if(detail!=null){foreach(var kv in Json.Map("turn",s.Turn,"direction",direction,"start",before.Cycle,"startSeed",before.Seed,"after",s.Cycle,"endSeed",s.Seed,"calls",s.Cycle-before.Cycle,"counterBefore",before.Counter,"counterAfter",s.Counter,"inhibitAfter",s.Inhibit,"lastDirectionAfter",s.LastDirection,"firstRate",firstRate,"secondRate",c.EncounterRate,"outcome",outcome,"draws",draws))detail[kv.Key]=kv.Value;}
      return s;
    }
    public Session ApplyTurns(Session s,IEnumerable<int> directions,CancellationToken token){var ds=directions.ToArray();var cs=new List<Candidate>(s.Candidates.Count);TurnState state=null;foreach(var c in s.Candidates){token.ThrowIfCancellationRequested();state=Initial(s.Config,c.Seed,c.Cycle);foreach(int d in ds){token.ThrowIfCancellationRequested();state=Move(state,s.Config,d);}var next=c.Copy();next.Seed=state.Seed;next.Cycle=state.Cycle;cs.Add(next);}var config=s.Config.Copy();if(state!=null){config.Facing=state.Facing;config.LastDirection=state.LastDirection;config.Counter=state.Counter;config.Inhibit=state.Inhibit;}return s.With(cs,config);}
    public Session Create(D setup,CancellationToken token,Action<string,int> progress) {
      uint seed=(uint)Json.Integer(Json.Require(setup,"seed"),0,uint.MaxValue,"seed");long first=Json.Integer(Json.Require(setup,"first"),0,uint.MaxValue,"first"),last=Json.Integer(Json.Require(setup,"last"),first,uint.MaxValue,"last"),target=Json.Integer(Json.Require(setup,"target"),0,uint.MaxValue,"target");var ctx=Context.Parse(Json.RequireObject(Json.Require(setup,"context")));Compile(ctx);
      if(ctx.Map!=217||Tiles[ctx.PlayerZ*32+ctx.PlayerX]!=8)throw new InvalidDataException("Checkpoint must be on encounterable Burned Tower B1F floor.");
      string answer=Json.Get(setup,"answer")==null?"no":Json.Text(setup,"answer"),entry=Json.Get(setup,"entry")==null?"ready":Json.Text(setup,"entry");if(!new[]{"yes","no"}.Contains(answer)||!new[]{"ready","arrival"}.Contains(entry))throw new InvalidDataException("Invalid checkpoint mode or answer.");
      var config=Configure(Json.Obj(Json.Get(setup,"turns")),ctx.PlayerFacing);bool arrival=entry=="arrival";if(arrival&&ctx.TalkFacing!=1)throw new InvalidDataException("Arrival route requires the follower south of the player.");
      var s=new Session{Seed=seed,First=first,Last=last,Target=target,Context=ctx,Config=config,Finish=Json.Get(setup,"finish")==null?null:TimingConfig.Parse(Json.RequireObject(Json.Get(setup,"finish"))),Answer=answer,Entry=entry,Arrival=arrival,ArrivalLastDirection=config.LastDirection,TalkFacing=ctx.TalkFacing,Candidates=new List<Candidate>()};
      s.Catalog=new ResponseCatalog(this,ctx,answer,s.Finish);uint current=Jump(seed,first);TurnState state=null;
      for(long origin=first;origin<=last;origin++){token.ThrowIfCancellationRequested();state=Initial(config,current,origin);if(arrival){state.Facing=1;state.Counter=0;state.Inhibit=0;state=Move(state,config,2);state=Move(state,config,2,"walk");state=Move(state,config,2,"walk");state=Move(state,config,0,"walk");}if(state.Cycle>target)throw new InvalidDataException("Arrival or starting advance passes the target.");s.Candidates.Add(new Candidate{Origin=origin,Cycle=state.Cycle,Seed=state.Seed,Context=ctx});current=Next(current);if(s.Candidates.Count%256==0&&progress!=null)progress("starts",s.Candidates.Count);}
      if(arrival&&state!=null){config=config.Copy();config.Facing=state.Facing;config.LastDirection=state.LastDirection;config.Counter=state.Counter;config.Inhibit=state.Inhibit;s.Config=config;}return s;
    }
  }
}
