using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using D=System.Collections.Generic.Dictionary<string,object>;
namespace HgssStrategyReader {
  // Per-checkpoint immutable presentation table. A different HP/status/profile,
  // answer or timing setup creates a new catalog; mood/friendship only choose
  // rules and apply numeric effects. Neither changes the rendered text.
  sealed class ResponseEntry {
    public int Id,CueId;public string Key,Label,SecondsText,Error;public List<Cue> Cues;public List<string> Texts;public D[] CueData;public Timing Timing;public int[][] Changes;
    public Context After(Context ctx){if(Error!=null)throw new InvalidDataException(Error);var mon=ctx.Mon.Copy();foreach(var change in Changes){mon.Mood=Math.Max(-127,Math.Min(127,mon.Mood+change[0]));mon.Friendship=Math.Max(0,Math.Min(255,mon.Friendship+change[1]));}return ctx.WithMon(mon);}
  }
  sealed class ResponseCatalog {
    readonly ResponseEntry[] entries;readonly GameModel game;readonly Dictionary<int,Rule[]> rules=new Dictionary<int,Rule[]>();
    public ResponseCatalog(GameModel game,Context context,string answer,TimingConfig timing){this.game=game;var actions=Json.Obj(game.Data["actions"]);int[] ids=actions.Keys.Select(int.Parse).OrderBy(i=>i).ToArray();entries=new ResponseEntry[ids.Last()+1];var cueIds=new Dictionary<string,int>(StringComparer.Ordinal);
      foreach(int id in ids){var entry=entries[id]=new ResponseEntry{Id=id};try{var done=game.Complete(id,context,answer);entry.Cues=done.Cues;entry.Texts=done.Texts;entry.Key=GameModel.CueKey(done.Cues);entry.Label=string.Join(" / ",done.Texts);entry.CueData=done.Cues.Select(c=>c.Data()).ToArray();entry.Timing=game.Estimate(id,context,answer,timing);int cueId;if(!cueIds.TryGetValue(entry.Key,out cueId)){cueId=cueIds.Count;cueIds.Add(entry.Key,cueId);}entry.CueId=cueId;var changes=new List<int[]>();int action=id;
          for(int depth=0;depth<6;depth++){var a=Json.Obj(actions[action.ToString()]);changes.Add(new[]{Json.Int(a,"mood"),Json.Int(a,"friendship")});action=Json.Truth(Json.Get(a,"question"))?Json.Int(a,answer):0;if(action==0)break;}entry.Changes=changes.ToArray();entry.SecondsText=entry.Timing.Seconds.HasValue?entry.Timing.Seconds.Value.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" s":"Timing unavailable";
        }catch(InvalidDataException e){entry.Error=e.Message;}}
    }
    public ResponseEntry this[int id]{get{if(id<0||id>=entries.Length||entries[id]==null)throw new InvalidDataException("Unknown response.");var r=entries[id];if(r.Error!=null)throw new InvalidDataException(r.Error);return r;}}
    public Rule[] Rules(Context context){int key=(context.Mon.Mood+127)*256+context.Mon.Friendship;lock(rules){Rule[] row;if(!rules.TryGetValue(key,out row))rules[key]=row=game.Compile(context);return row;}}
  }
}
