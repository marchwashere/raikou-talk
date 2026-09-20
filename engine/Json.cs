using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HgssStrategyReader {
  // Plain JSON data codec. No scripting, web assemblies, or external packages.
  static class Json {
    public static Dictionary<string, object> Parse(string text) { return RequireObject(Decode(text)); }
    public static object Decode(string text) { return new Parser(text).Read(); }
    public static Dictionary<string,object> Obj(object value) { return value as Dictionary<string,object>; }
    public static Dictionary<string,object> RequireObject(object value) { var d=Obj(value); if(d==null) throw new InvalidDataException("Expected a JSON object."); return d; }
    public static object Get(Dictionary<string,object> d,string key) { object value; return d!=null && d.TryGetValue(key,out value) ? value : null; }
    public static object Require(Dictionary<string,object> d,string key) { if(d==null||!d.ContainsKey(key)) throw new InvalidDataException("Missing field: "+key); return d[key]; }
    public static string Text(Dictionary<string,object> d,string key) { return Convert.ToString(Get(d,key),CultureInfo.InvariantCulture)??""; }
    public static int Int(Dictionary<string,object> d,string key) { return Convert.ToInt32(Get(d,key),CultureInfo.InvariantCulture); }
    public static bool Bool(Dictionary<string,object> d,string key) { return Get(d,key) is bool&&(bool)Get(d,key); }
    public static bool Truth(object v) { if(v==null) return false; if(v is bool) return (bool)v; if(v is string) return ((string)v).Length>0; if(v is IConvertible) return Convert.ToDouble(v,CultureInfo.InvariantCulture)!=0; return true; }
    public static double Number(object v) { if(v==null||v is bool||v is string) throw new InvalidDataException("Expected a number."); return Convert.ToDouble(v,CultureInfo.InvariantCulture); }
    public static long Integer(object v,long min,long max,string name) { double n=Number(v); if(double.IsNaN(n)||double.IsInfinity(n)||n<min||n>max||n!=Math.Floor(n)) throw new InvalidDataException("Invalid "+name+"."); return (long)n; }
    public static long InputInteger(object v,long min,long max,string name) { if(v is string) { long n; if(!long.TryParse(((string)v).Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out n)||n<min||n>max) throw new InvalidDataException("Invalid "+name+"."); return n; } return Integer(v,min,max,name); }
    public static IEnumerable<object> Items(object v) { var a=v as IEnumerable; return a==null||v is string||v is IDictionary ? Enumerable.Empty<object>() : a.Cast<object>(); }
    public static object[] Array(object v) { var a=v as object[]; if(a==null) throw new InvalidDataException("Expected a JSON array."); return a; }
    public static Dictionary<string,object> Map(params object[] values) { var d=new Dictionary<string,object>(StringComparer.Ordinal); for(int i=0;i<values.Length;i+=2)d.Add((string)values[i],values[i+1]); return d; }
    public static string Encode(object value) { var b=new StringBuilder(); Write(b,value,0); return b.ToString(); }
    public static string Quote(string value) { var b=new StringBuilder(); String(b,value??""); return b.ToString(); }
    static void String(StringBuilder b,string value) {
      b.Append('"');
      for(int i=0;i<value.Length;i++) { char c=value[i];
        switch(c) { case '"':b.Append("\\\"");break; case '\\':b.Append("\\\\");break;case '\b':b.Append("\\b");break;case '\f':b.Append("\\f");break;case '\n':b.Append("\\n");break;case '\r':b.Append("\\r");break;case '\t':b.Append("\\t");break;
          default: if(c<32||(char.IsSurrogate(c)&&!(char.IsHighSurrogate(c)&&i+1<value.Length&&char.IsLowSurrogate(value[i+1]))&&!(char.IsLowSurrogate(c)&&i>0&&char.IsHighSurrogate(value[i-1])))) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); break; }
      } b.Append('"');
    }
    static void Write(StringBuilder b,object v,int depth) {
      if(depth>512) throw new InvalidDataException("JSON nesting is too deep.");
      if(v==null) {b.Append("null");return;} if(v is string) {String(b,(string)v);return;} if(v is bool) {b.Append((bool)v?"true":"false");return;}
      var d=v as IDictionary; if(d!=null) {b.Append('{');bool first=true;foreach(DictionaryEntry item in d){if(!first)b.Append(',');first=false;String(b,Convert.ToString(item.Key,CultureInfo.InvariantCulture));b.Append(':');Write(b,item.Value,depth+1);}b.Append('}');return;}
      var a=v as IEnumerable;if(a!=null){b.Append('[');bool first=true;foreach(var item in a){if(!first)b.Append(',');first=false;Write(b,item,depth+1);}b.Append(']');return;}
      if(v is double||v is float){double n=Convert.ToDouble(v);if(double.IsNaN(n)||double.IsInfinity(n))throw new InvalidDataException("Non-finite JSON number.");b.Append(n.ToString("R",CultureInfo.InvariantCulture));return;}
      if(v is IConvertible){b.Append(Convert.ToString(v,CultureInfo.InvariantCulture));return;}
      // App-local status/settings objects only; imported data is never reflected.
      var props=v.GetType().GetProperties().Where(p=>p.GetIndexParameters().Length==0&&p.CanRead);
      Write(b,props.ToDictionary(p=>p.Name,p=>p.GetValue(v,null)),depth+1);
    }
    sealed class Parser {
      readonly string text; int pos;
      public Parser(string text) { this.text=text??""; }
      void Space(){while(pos<text.Length&&(text[pos]==' '||text[pos]=='\t'||text[pos]=='\r'||text[pos]=='\n'))pos++;}
      Exception Error(){return new InvalidDataException("Invalid JSON near character "+pos+".");}
      public object Read(){Space();if(pos<text.Length&&text[pos]=='\ufeff')pos++;var v=Value(0);Space();if(pos!=text.Length)throw Error();return v;}
      object Value(int depth) {
        if(depth>512)throw new InvalidDataException("JSON nesting is too deep.");Space();if(pos==text.Length)throw Error();char c=text[pos];
        if(c=='"')return String();
        if(c=='{'){pos++;var d=new Dictionary<string,object>(StringComparer.Ordinal);Space();if(Take('}'))return d;while(true){Space();if(pos==text.Length||text[pos]!='"')throw Error();string k=String();Space();if(!Take(':')||d.ContainsKey(k))throw Error();d.Add(k,Value(depth+1));Space();if(Take('}'))break;if(!Take(','))throw Error();}return d;}
        if(c=='['){pos++;var a=new List<object>();Space();if(Take(']'))return a.ToArray();while(true){a.Add(Value(depth+1));Space();if(Take(']'))break;if(!Take(','))throw Error();}return a.ToArray();}
        if(c=='t'){Literal("true");return true;}if(c=='f'){Literal("false");return false;}if(c=='n'){Literal("null");return null;}
        int start=pos;Take('-');if(!Take('0')){if(pos==text.Length||text[pos]<'1'||text[pos]>'9')throw Error();while(pos<text.Length&&Digit(text[pos]))pos++;}
        if(Take('.')){int n=pos;while(pos<text.Length&&Digit(text[pos]))pos++;if(n==pos)throw Error();}
        if(Take('e')||Take('E')){if(!Take('+'))Take('-');int n=pos;while(pos<text.Length&&Digit(text[pos]))pos++;if(n==pos)throw Error();}
        string token=text.Substring(start,pos-start);long integer;if(long.TryParse(token,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out integer))return integer;
        double number;if(!double.TryParse(token,NumberStyles.Float,CultureInfo.InvariantCulture,out number)||double.IsInfinity(number)||double.IsNaN(number))throw Error();return number;
      }
      static bool Digit(char c){return c>='0'&&c<='9';}
      bool Take(char c){if(pos<text.Length&&text[pos]==c){pos++;return true;}return false;}
      void Literal(string value){if(pos+value.Length>text.Length||text.Substring(pos,value.Length)!=value)throw Error();pos+=value.Length;}
      string String(){pos++;var b=new StringBuilder();while(pos<text.Length){char c=text[pos++];if(c=='"')return b.ToString();if(c<32)throw Error();if(c!='\\'){b.Append(c);continue;}if(pos==text.Length)throw Error();c=text[pos++];switch(c){case '"':case '\\':case '/':b.Append(c);break;case 'b':b.Append('\b');break;case 'f':b.Append('\f');break;case 'n':b.Append('\n');break;case 'r':b.Append('\r');break;case 't':b.Append('\t');break;case 'u':if(pos+4>text.Length)throw Error();int n;if(!int.TryParse(text.Substring(pos,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out n))throw Error();pos+=4;b.Append((char)n);break;default:throw Error();}}throw Error();}
    }
  }
}
