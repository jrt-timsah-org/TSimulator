using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using TSimulator.Core;

namespace TSimulator.Desktop;

public sealed partial class SimulatorApp
{
    private sealed record PresetEntry(RobotPreset Preset,bool User);
    private RobotModuleCatalog moduleCatalog=null!;
    private List<PresetEntry> robotPresets=[];
    private RobotAssembly builderAssembly=new();
    private RobotSpec builderSpec=new();
    private bool builderInitialized,builderAll,builderNameEditing,builderPickerDrawing;
    private string builderName="カスタム機体", builderSource="現在の機体";
    private string? builderSavedId,builderError;
    private int builderPresetIndex=-1;
    private RenderTexture2D builderView;
    private ModuleSlot? modulePicker;
    private float modulePickerScroll;
    private bool? builderValidatedSandbox;
    private RuleProfile? builderValidatedRules;
    private static readonly Dictionary<ModuleSlot,string> SlotNames=new()
    { [ModuleSlot.Chassis]="シャーシ",[ModuleSlot.Drive]="駆動",[ModuleSlot.Shooter]="射出機構",[ModuleSlot.Magazine]="マガジン",[ModuleSlot.Arm]="アーム" };
    private IEnumerable<string> BuilderGlyphs => moduleCatalog.Modules.Select(m=>m.Name+m.Description)
        .Concat(robotPresets.Select(p=>p.Preset.Name)).Append(builderName).Append(builderSpec.Name);
    private RobotSpec EditableRobot => builderAll?draft.Robot:draft.RobotOverrides.GetValueOrDefault(selected,draft.Robot);
    private void EditRobot(Func<RobotSpec,RobotSpec> edit)
    {
        var robot=edit(EditableRobot) with { Assembly=null };
        if(builderAll)draft=draft with { Robot=robot,RobotOverrides=[] };
        else draft=draft with { RobotOverrides=new(draft.RobotOverrides) { [selected]=robot } };
        builderInitialized=false;
    }

    private void LoadBuilderCatalog()
    {
        var bundled=Directory.GetFiles(Path.Combine(Paths.Content,"config","modules"),"*.json").Order(StringComparer.Ordinal).ToArray();
        moduleCatalog=RobotModuleCatalog.Load(bundled);
        try
        {
            if(Directory.Exists(Paths.Modules))moduleCatalog=RobotModuleCatalog.Load(bundled.Concat(Directory.GetFiles(Paths.Modules,"*.json").Order(StringComparer.Ordinal)));
        }
        catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){message="ユーザーモジュール読込失敗: "+e.Message;}
        var entries=new Dictionary<string,PresetEntry>(StringComparer.Ordinal);
        foreach(var (folder,user) in new[] { (Path.Combine(Paths.Content,"config","robots"),false),(Paths.Robots,true) })
        {
            if(!Directory.Exists(folder))continue;
            foreach(var path in Directory.GetFiles(folder,"*.json").Order(StringComparer.Ordinal).Take(128))
            {
                try
                {
                    if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("Preset exceeds 1 MiB.");
                    var preset=JsonFiles.Load<RobotPreset>(path);
                    if(string.IsNullOrWhiteSpace(preset.Id) || preset.Id.Length>80 || preset.Id.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
                        || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length>64 || preset.Robot is null)throw new InvalidDataException("Invalid preset metadata.");
                    preset.Robot.Validate(new(),sandbox:true);
                    entries[preset.Id]=new(preset,user);
                }
                catch(Exception e)when(e is IOException or JsonException){message="プリセット読込失敗: "+e.Message;}
            }
        }
        robotPresets=entries.Values.OrderBy(p=>p.Preset.Id=="standard"?0:1).ThenBy(p=>p.Preset.Name,StringComparer.Ordinal).ToList();
    }

    private void InitializeBuilder()
    {
        builderSpec=builderAll?draft.Robot:draft.RobotOverrides.GetValueOrDefault(selected,draft.Robot);
        builderName=builderSpec.Name;builderSource="現在の機体";builderPresetIndex=-1;builderSavedId=null;
        builderAssembly=builderSpec.Assembly is { } assembly && Enum.GetValues<ModuleSlot>().All(s=>assembly.Modules is not null && assembly.Modules.TryGetValue(s,out var id) && moduleCatalog.Modules.Any(m=>m.Id==id && m.Slot==s))
            ? assembly with { Modules=new(assembly.Modules) } : new();
        builderInitialized=true; ValidateBuilder();
    }
    private void ValidateBuilder()
    {
        builderValidatedSandbox=draft.Sandbox;builderValidatedRules=draft.Rules;
        try { builderSpec.Validate(draft.Rules,draft.Sandbox);builderError=null; }
        catch(IOException e){builderError=e.Message;}
    }
    private void StageBuilder()
    {
        builderSpec=builderSpec with { Name=string.IsNullOrWhiteSpace(builderName)?"カスタム機体":builderName.Trim() };
        if(builderAll)draft=draft with { Robot=builderSpec,RobotOverrides=[] };
        else
        {
            var overrides=new Dictionary<int,RobotSpec>(draft.RobotOverrides) { [selected]=builderSpec };
            draft=draft with { RobotOverrides=overrides };
        }
        ValidateBuilder();
    }
    private void ChooseModule(ModuleSlot slot,string id)
    {
        var modules=new Dictionary<ModuleSlot,string>(builderAssembly.Modules) { [slot]=id };
        var next=builderAssembly with { Modules=modules };
        try
        {
            var built=moduleCatalog.Build(next,builderName);
            builderAssembly=next;builderSpec=built;builderSource="モジュール構成";StageBuilder();
        }
        catch(IOException e){message="組み立てできません: "+e.Message;}
    }
    private void ChoosePreset(int index)
    {
        if(robotPresets.Count==0)return;
        var entry=robotPresets[(index%robotPresets.Count+robotPresets.Count)%robotPresets.Count];
        builderSpec=entry.Preset.Robot;builderName=entry.Preset.Name;
        StageBuilder();builderInitialized=false;InitializeBuilder();
        builderSource=entry.Preset.Name;builderSavedId=entry.User?entry.Preset.Id:null;builderPresetIndex=robotPresets.IndexOf(entry);
        ReloadBuilderFont();
    }
    private void SaveRobotPreset()
    {
        try
        {
            StageBuilder();builderSpec.Validate(draft.Rules,sandbox:true);
            builderName=builderSpec.Name;
            var id=builderSavedId??"user-"+Guid.NewGuid().ToString("N");
            JsonFiles.Save(Path.Combine(Paths.Robots,id+".json"),new RobotPreset(id,builderName,builderSpec));
            LoadBuilderCatalog();builderSavedId=id;builderPresetIndex=robotPresets.FindIndex(p=>p.Preset.Id==id);
            message="機体プリセットを保存しました / "+Paths.Robots;ReloadBuilderFont();
        }
        catch(Exception e)when(e is IOException or JsonException){message="保存失敗: "+e.Message;}
    }
    private void ReloadBuilderFont()
    {
        var path=Path.Combine(Paths.Content,"assets","fonts","NotoSansJP-Regular.otf");
        if(font.Texture.Id==0 || !File.Exists(path))return;
        var glyphs=UiGlyphs();var replacement=Raylib.LoadFontEx(path,32,glyphs,glyphs.Length);
        Raylib.SetTextureFilter(replacement.Texture,TextureFilter.Bilinear);Raylib.UnloadFont(font);font=replacement;
    }
    private int[] UiGlyphs()
    {
        var path=Path.Combine(Paths.Content,"assets","fonts","ui-glyphs.txt");
        var text=(File.Exists(path)?File.ReadAllText(path):string.Concat(Labels.Values))+string.Concat(BuilderGlyphs);
        return text.EnumerateRunes().Where(r=>!System.Text.Rune.IsControl(r)).Select(r=>r.Value).Concat(Enumerable.Range(32,95)).Distinct().ToArray();
    }

    private void DrawBuilder(int x,int y)
    {
        if(!builderInitialized)InitializeBuilder();
        if(builderValidatedSandbox!=draft.Sandbox || builderValidatedRules!=draft.Rules)ValidateBuilder();
        if(Button(builderAll?"編集対象: 全機体に共通":$"編集対象: 選択機体 {sim.Robots[selected].Side} / {selected+1:00}",x+26,y+125,450,32,tooltip:"全機体を選ぶと機体別の設定を解除します。適用すると新しいラウンドを開始します。"))
        { builderAll=!builderAll;InitializeBuilder();StageBuilder(); }
        if(Button("モジュール再読込",x+746,y+125,220,32,tooltip:"config/modules とユーザー領域 modules のJSONを再読込。ユーザー定義は同じIDの標準モジュールを上書きできます。"))
        { LoadBuilderCatalog();builderInitialized=false;InitializeBuilder();ReloadBuilderFont();message="モジュールを再読込しました"; }
        int row=0;
        foreach(var slot in Enum.GetValues<ModuleSlot>())
        {
            var py=y+172+row++*66;var options=moduleCatalog.Modules.Where(m=>m.Slot==slot).ToArray();
            var current=Array.FindIndex(options,m=>m.Id==builderAssembly.Modules[slot]);
            var module=options[Math.Max(0,current)];
            Text(SlotNames[slot],x+26,py,13,Muted);
            var tooltip=$"{module.Name} / {module.Id}\n{module.Description}\n追加質量 {module.AddedMassKg:0.0} kg";
            if(Button("<",x+26,py+21,33,32))ChooseModule(slot,options[(current-1+options.Length)%options.Length].Id);
            if(Button(module.Name,x+67,py+21,368,32,tooltip:tooltip)){modulePicker=slot;modulePickerScroll=0;builderNameEditing=false;}
            if(Button(">",x+443,py+21,33,32))ChooseModule(slot,options[(current+1)%options.Length].Id);
        }
        Text("プリセット / 選択すると構成を切替",x+26,y+510,13,Muted);
        if(Button("<",x+26,y+532,33,32,enabled:robotPresets.Count>0))ChoosePreset(builderPresetIndex<0?robotPresets.Count-1:builderPresetIndex-1);
        if(Button(builderSource,x+67,y+532,368,32,enabled:robotPresets.Count>0,tooltip:"クリックまたは左右ボタンで初期・保存済みプリセットを切り替えます。"))ChoosePreset(builderPresetIndex+1);
        if(Button(">",x+443,y+532,33,32,enabled:robotPresets.Count>0))ChoosePreset(builderPresetIndex+1);
        var nameRect=new Rectangle(x+26,y+575,290,28);
        if(Button(builderNameEditing?builderName+"|":builderName,nameRect.X,nameRect.Y,nameRect.Width,nameRect.Height,builderNameEditing,tooltip:"保存する機体名を入力します。Enterで確定、Escapeで入力を終了。"))builderNameEditing=true;
        if(builderNameEditing)
        {
            var changed=false;int key;
            while((key=Raylib.GetCharPressed())!=0)
            { if(builderName.Length<32 && key>=32){builderName+=char.ConvertFromUtf32(key);changed=true;} }
            if(Raylib.IsKeyPressed(KeyboardKey.Backspace) && builderName.Length>0)
            { var count=builderName.Length>1 && char.IsLowSurrogate(builderName[^1]) && char.IsHighSurrogate(builderName[^2])?2:1;builderName=builderName[..^count];changed=true; }
            if(Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Escape) || (Raylib.IsMouseButtonPressed(MouseButton.Left)&&!Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(),nameRect)))builderNameEditing=false;
            if(changed){StageBuilder();ReloadBuilderFont();}
        }
        if(Button("プリセット保存",x+327,y+575,149,28,tooltip:builderSavedId is null?"ユーザー領域に新しいJSONプリセットとして保存します。":"選択したユーザープリセットを更新します。"))SaveRobotPreset();

        DrawBuilderPreview(x+516,y+172,450,195);
        Text("性能 / モジュールの変更を即時反映",x+516,y+382,16,Accent);
        var s=builderSpec;
        var effectiveInterval=Math.Ceiling((double)s.ShotInterval*draft.Rules.TickRate-.000001)/draft.Rules.TickRate;
        string[] stats=[$"外形 {s.Width:0.00} × {s.Depth:0.00} × {s.Height:0.00} m / {s.MassKg:0.0} kg",
            $"走行 {s.MaxSpeed:0.0} m/s / 加速度 {s.Acceleration:0.0} m/s² / {(s.Holonomic?"全方向":"差動")}",
            $"射撃 {1/effectiveInterval:0.0} 発/秒 / 設定間隔 {s.ShotInterval:0.00} 秒",
            $"初速 {s.ShotSpeed:0.0} m/s / 射出高さ {s.ShotHeight:0.000} m",
            $"マガジン {s.MagazineCapacity}枚 / 初期装填は同盟予備から",
            $"アーム {s.ArmReach:0.00} m / 高さ {s.ArmMinHeight:0.00}〜{s.ArmMaxHeight:0.00} m"];
        for(var i=0;i<stats.Length;i++)Text(stats[i],x+516,y+415+i*24,14);
        Text(builderError is null?draft.Sandbox?"自由練習 / パラメータ有効":"競技パラメータ範囲内（機構検査は別途必要）":"競技規定外 / 自由練習へ切替が必要",x+516,y+565,14,builderError is null?Accent:Warning);
        Text("適用で新ラウンド。連射でもパネルは最大3Hz。",x+516,y+590,13,Muted);
        if(modulePicker is not null)DrawModulePicker(x,y);
    }

    private void DrawBuilderPreview(int x,int y,int width,int height)
    {
        if(builderView.Id==0)builderView=Raylib.LoadRenderTexture(width,height);
        Raylib.BeginTextureMode(builderView);Raylib.ClearBackground(new(27,40,57,255));
        var camera=new Camera3D { Position=new(1.65f,1.2f,1.6f),Target=new(.08f,.38f,0),Up=Vector3.UnitY,FovY=40,Projection=CameraProjection.Perspective };
        Raylib.BeginMode3D(camera);renderer.DrawPreview(builderSpec);Raylib.EndMode3D();Raylib.EndTextureMode();
        Raylib.DrawTexturePro(builderView.Texture,new(0,0,width,-height),new(x,y,width,height),Vector2.Zero,0,Color.White);
        Text("モジュール外観 / 衝突判定は機体外形",x+12,y+12,12,Muted);
    }
    private void DrawModulePicker(int x,int y)
    {
        var slot=modulePicker!.Value;builderPickerDrawing=true;
        Fill(x+506,y+165,470,438,new(12,19,30,255));Text(SlotNames[slot]+"を選択",x+520,y+180,20);
        if(Button("閉じる",x+867,y+177,96,28))modulePicker=null;
        var options=moduleCatalog.Modules.Where(m=>m.Slot==slot).ToArray();
        var area=new Rectangle(x+516,y+220,450,367);
        if(Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(),area))modulePickerScroll-=Raylib.GetMouseWheelMove()*40;
        modulePickerScroll=Math.Clamp(modulePickerScroll,0,Math.Max(0,options.Length*79-area.Height));
        hitClip=area;Raylib.BeginScissorMode((int)area.X,(int)area.Y,(int)area.Width,(int)area.Height);
        for(var i=0;i<options.Length;i++)
        {
            var module=options[i];var py=area.Y+i*79-modulePickerScroll;
            if(Button(module.Name,area.X,py,area.Width,34,builderAssembly.Modules[slot]==module.Id,tooltip:module.Description))
            { ChooseModule(slot,module.Id);modulePicker=null; }
            FittedText(module.Description,area.X+10,py+43,area.Width-20,13,Muted);
            Text($"追加 {module.AddedMassKg:0.0} kg / {module.Id}",area.X+10,py+61,area.Width-20>300?11:10,Muted);
        }
        Raylib.EndScissorMode();hitClip=null;builderPickerDrawing=false;
    }
}
