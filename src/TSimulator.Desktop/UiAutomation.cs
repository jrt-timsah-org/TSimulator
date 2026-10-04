using System.Globalization;
using Raylib_cs;

namespace TSimulator.Desktop;

/// <summary>Bounded raylib UI events for repeatable smoke flows, parsed and validated before native playback.</summary>
internal static class UiAutomation
{
    public static unsafe AutomationEvent[] Load(string path,int frames)
    {
        if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("Automation file exceeds 1 MiB.");
        var events=new List<AutomationEvent>();int? expected=null;
        foreach(var raw in File.ReadLines(path))
        {
            var tokens=raw.Split('#')[0].Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            if(tokens.Length==0)continue;
            if(tokens.Length==2 && tokens[0]=="c") { expected=int.Parse(tokens[1],CultureInfo.InvariantCulture);continue; }
            if(tokens.Length!=7 || tokens[0]!="e" || events.Count>=10000)throw new InvalidDataException("Invalid UI automation event.");
            var e=new AutomationEvent { Frame=uint.Parse(tokens[1],CultureInfo.InvariantCulture),Type=uint.Parse(tokens[2],CultureInfo.InvariantCulture) };
            for(var i=0;i<4;i++)e.Params[i]=int.Parse(tokens[i+3],CultureInfo.InvariantCulture);
            if(e.Frame>=frames || (events.Count>0 && e.Frame<events[^1].Frame))throw new InvalidDataException("UI automation frames are out of range or order.");
            var valid=e.Type switch
            {
                1 or 2=>e.Params[0] is >= 0 and <= 348,
                5 or 6=>e.Params[0] is >= 0 and <= 6,
                7=>e.Params[0] is >= 0 and <= 2560 && e.Params[1] is >= 0 and <= 1440,
                8=>Math.Abs((long)e.Params[0])<=10 && Math.Abs((long)e.Params[1])<=10,
                21=>e.Params[0] is >= 1100 and <= 2560 && e.Params[1] is >= 760 and <= 1440,
                _=>false
            };
            if(!valid)throw new InvalidDataException("Unsupported or invalid UI automation input.");
            events.Add(e);
        }
        if(events.Count==0 || (expected.HasValue && expected!=events.Count))throw new InvalidDataException("UI automation event count mismatch.");
        return events.ToArray();
    }
}
