using System.Diagnostics;
using System.Numerics;
using Raylib_cs;
using TSimulator.Core;
namespace TSimulator.Desktop;

public sealed class SimulatorApp
{
    private static readonly Color Bg = new(12, 19, 30, 255), Panel = new(19, 29, 43, 255), Muted = new(135, 157, 180, 255), White = new(229, 239, 246, 255), Accent = new(106, 237, 183, 255);
    private static readonly (Skill Skill, string Name, int Cost)[] Skills = [(Skill.PitIn,"ピットイン",20),(Skill.Supply,"サプライ",100),
        (Skill.Healing1,"回復 Lv.1",50),(Skill.Healing2,"回復 Lv.2",100),(Skill.Boost1,"ブースト Lv.1",50),
        (Skill.Boost2,"ブースト Lv.2",90),(Skill.Barrier1,"バリア Lv.1",80),(Skill.Barrier2,"バリア Lv.2",150),(Skill.Regenerate,"リジェネ",100)];
    private static readonly Dictionary<string,string> Labels = new()
    {
        ["Forward"]="前進",["Backward"]="後退",["Left"]="左移動",["Right"]="右移動",["TurnLeft"]="左旋回",["TurnRight"]="右旋回",
        ["CameraUp"]="カメラ上",["CameraDown"]="カメラ下",["CameraLeft"]="カメラ左",["CameraRight"]="カメラ右",
        ["Fire"]="射撃",["Interact"]="回収・設置",["Drop"]="コンテナを落とす",["ArmUp"]="アーム上",["ArmDown"]="アーム下",
        ["CameraMode"]="視点切替",["SwitchRobot"]="操縦ロボット切替",["Pause"]="一時停止",["Reset"]="ラウンド再開",
        ["Settings"]="設定",["EmergencyStop"]="非常停止",["Record"]="記録開始・終了",["ReloadConfig"]="シナリオ再読込"
    };
    private readonly string[] args;
    private Scenario scenario;
    private Simulation sim;
    private AppSettings settings;
    private Scenario draft;
    private string? scenarioPath;
    private WorldRenderer renderer = null!;
    private Font font;
    private RenderTexture2D view;
    private readonly PracticeBot bot = new();
    private RobotCommand[] commands;
    private readonly Dictionary<string, KeyboardKey> keys = [];
    private int selected, cameraMode = 1, settingsTab, selectedCell;
    private bool paused, menu, quit, pendingInteract, pendingDrop, pendingStop;
    private Skill pendingSkill;
    private string? binding;
    private float cameraYaw, cameraPitch = -.03f, distance = 4, timeScale = 1;
    private double accumulator;
    private ReplayWriter? recorder;
    private string message = "F1 で設定 / C で視点切替 / F で回収・設置";
    private Task? download;
    private int smokeFrames;
    private readonly string? screenshot;
    private bool noOfficial;
    public SimulatorApp(string[] args)
    {
        this.args = args;
        string? Option(string name) { var i = Array.IndexOf(args,name); return i>=0 && i+1<args.Length ? args[i+1] : null; }
        scenarioPath = Option("--scenario");
        scenario = scenarioPath is not null ? JsonFiles.Load<Scenario>(scenarioPath) : new Scenario();
        settings = File.Exists(Paths.Settings) ? JsonFiles.Load<AppSettings>(Paths.Settings) : new();
        settings.Validate(); draft = scenario; sim = new(scenario); commands = new RobotCommand[sim.Robots.Count];
        smokeFrames = int.TryParse(Option("--smoke"),out var frames) ? Math.Clamp(frames,1,10000) : 0;
        screenshot = Option("--screenshot"); noOfficial = args.Contains("--no-official");
        if (smokeFrames > 0) cameraMode = 2;
        menu = args.Contains("--settings");
        BindKeys();
    }
    private void BindKeys()
    {
        foreach (var pair in settings.Keys)
        {
            if (!Enum.TryParse<KeyboardKey>(pair.Value, true, out var key) || key == KeyboardKey.Null || !Enum.IsDefined(key))
                throw new InvalidDataException($"Unknown key for {pair.Key}: {pair.Value}");
            keys[pair.Key] = key;
        }
    }
    private bool Down(string action) => Raylib.IsKeyDown(keys[action]);
    private bool Pressed(string action) => Raylib.IsKeyPressed(keys[action]);
    private float Axis(string positive, string negative) => (Down(positive)?1:0)-(Down(negative)?1:0);
    public int Run()
    {
        if (args.Contains("--help")) { Console.WriteLine("TSimulator.Desktop [--scenario FILE] [--smoke FRAMES --screenshot PNG] [--settings] [--no-official]"); return 0; }
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(1440,900,"TSimulator | CoRE-2 2027");
        Raylib.SetWindowMinSize(1100,760); Raylib.SetExitKey(KeyboardKey.Null); Raylib.SetTargetFPS(settings.TargetFps);
        var glyphFile = Path.Combine(Paths.Content,"assets","fonts","ui-glyphs.txt");
        var glyphs = (File.Exists(glyphFile) ? File.ReadAllText(glyphFile) : string.Concat(Labels.Values))
            .Select(c=>(int)c).Concat(Enumerable.Range(32,95)).Distinct().ToArray();
        var fontPath = Path.Combine(Paths.Content,"assets","fonts","NotoSansJP-Regular.otf");
        font = File.Exists(fontPath) ? Raylib.LoadFontEx(fontPath,32,glyphs,glyphs.Length) : Raylib.GetFontDefault();
        Raylib.SetTextureFilter(font.Texture,TextureFilter.Bilinear);
        renderer = new(!noOfficial);
        int frame = 0;
        try
        {
            while (!quit && !Raylib.WindowShouldClose())
            {
                var dt = Math.Min(Raylib.GetFrameTime(), .1f);
                HandleInput(dt);
                if (download is { IsCompleted: true })
                {
                    if (download.IsCompletedSuccessfully) { renderer.Reload(); message="公式CADの取得完了"; }
                    else message="CAD取得失敗: " + download.Exception?.GetBaseException().Message;
                    download = null;
                }
                if (!paused && !menu && (Raylib.IsWindowFocused() || smokeFrames>0))
                {
                    accumulator += dt*timeScale;
                    int steps=0;
                    while (accumulator >= sim.DeltaTime && steps++ < 32 && !sim.Finished)
                    {
                        Tick(); accumulator -= sim.DeltaTime;
                    }
                    if (steps >= 32 || sim.Finished) accumulator=0;
                }
                else accumulator=0;
                Draw((float)Math.Clamp(accumulator / sim.DeltaTime,0,1));
                frame++;
                if (smokeFrames>0 && frame>=smokeFrames)
                {
                    if (screenshot is not null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot))!);
                        var captured = Raylib.LoadImageFromScreen();
                        try { if (!Raylib.ExportImage(captured, Path.GetFullPath(screenshot))) throw new IOException("Screenshot export failed."); }
                        finally { Raylib.UnloadImage(captured); }
                    }
                    Console.WriteLine($"SMOKE OK: frame={frame}, tick={sim.Tick}, official={renderer.OfficialLoaded}, hash={sim.StateHash()}");
                    quit=true;
                }
            }
        }
        finally
        {
            StopRecording(); renderer.Dispose();
            if (view.Id!=0) Raylib.UnloadRenderTexture(view);
            if (File.Exists(fontPath)) Raylib.UnloadFont(font);
            Raylib.CloseWindow();
        }
        return 0;
    }
    private void HandleInput(float dt)
    {
        if (binding is not null)
        {
            var key = (KeyboardKey)Raylib.GetKeyPressed();
            if (key != KeyboardKey.Null)
            {
                if (key != KeyboardKey.Escape)
                {
                    var duplicate = settings.Keys.FirstOrDefault(x => x.Value.Equals(key.ToString(),StringComparison.OrdinalIgnoreCase)).Key;
                    if (duplicate is not null && duplicate != binding) settings.Keys[duplicate]=settings.Keys[binding];
                    settings.Keys[binding]=key.ToString(); BindKeys(); message="キー割り当てを変更しました";
                }
                binding=null;
            }
            return;
        }
        if (Pressed("Settings") || Raylib.IsKeyPressed(KeyboardKey.Escape)) { menu=!menu; draft=scenario; }
        if (menu) return;
        if (Pressed("Pause")) paused=!paused;
        if (Pressed("Reset")) Reset(scenario);
        if (Pressed("ReloadConfig") && scenarioPath is not null)
        {
            try { Reset(JsonFiles.Load<Scenario>(scenarioPath)); message="シナリオを再読込しました"; }
            catch(Exception e) when(e is IOException or System.Text.Json.JsonException) { message=e.Message; }
        }
        if (Pressed("Record"))
        {
            if (recorder is not null) StopRecording();
            else { Reset(scenario); var path=Path.Combine(Paths.Replays,$"practice-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl"); recorder=new(path,scenario); message="記録中: "+path; }
        }
        if (Pressed("SwitchRobot")) { selected=(selected+1)%sim.Robots.Count; cameraYaw=0; cameraPitch=-.03f; }
        if (Pressed("CameraMode")) cameraMode=(cameraMode+1)%3;
        if (Pressed("Interact")) pendingInteract=true;
        if (Pressed("Drop")) pendingDrop=true;
        if (Pressed("EmergencyStop")) pendingStop=true;
        for(var i=0;i<9;i++) if(Pressed(Skills[i].Skill.ToString())) pendingSkill=Skills[i].Skill;
        cameraYaw=Math.Clamp(cameraYaw+Axis("CameraRight","CameraLeft")*dt*settings.CameraSensitivity,-2.8f,2.8f);
        cameraPitch=Math.Clamp(cameraPitch+Axis("CameraUp","CameraDown")*dt*settings.CameraSensitivity,-1.1f,.7f);
        if (Raylib.IsMouseButtonDown(MouseButton.Right))
        {
            var mouse=Raylib.GetMouseDelta(); cameraYaw=Math.Clamp(cameraYaw+mouse.X*.003f,-2.8f,2.8f); cameraPitch=Math.Clamp(cameraPitch-mouse.Y*.003f,-1.1f,.7f);
        }
        distance=Math.Clamp(distance-Raylib.GetMouseWheelMove()*.4f,1.5f,12);
    }
    private void Tick()
    {
        for(var i=0;i<commands.Length;i++)
            commands[i]=i==selected ? new(i,Axis("Forward","Backward"),Axis("Right","Left"),Axis("TurnRight","TurnLeft"),
                Fire:Down("Fire"),Interact:pendingInteract,Drop:pendingDrop,Arm:Axis("ArmUp","ArmDown"),
                AimYaw:cameraMode==1?Math.Clamp(cameraYaw,-.8f,.8f):0,AimPitch:cameraMode==1?Math.Clamp(cameraPitch,-.3f,.5f):0,
                SpotCell:selectedCell,Skill:pendingSkill,EmergencyStop:pendingStop)
                : scenario.BotsEnabled ? bot.GetCommand(sim,sim.Robots[i]) : new(i);
        recorder?.Write(commands); sim.Step(commands);
        pendingInteract=pendingDrop=pendingStop=false; pendingSkill=Skill.None;
    }
    private void StopRecording()
    {
        if(recorder is null)return;
        recorder.Complete(sim); recorder.Dispose(); recorder=null; message="記録を保存しました: "+Paths.Replays;
    }
    private void Reset(Scenario next)
    {
        var replacement=new Simulation(next); // Validate before changing the running session.
        StopRecording(); sim=replacement; scenario=next; draft=next; commands=new RobotCommand[sim.Robots.Count];
        selected=0; accumulator=0; pendingInteract=pendingDrop=pendingStop=false; pendingSkill=Skill.None;
        cameraYaw=0; cameraPitch=-.03f;
    }
    private void Text(string text,float x,float y,float size=18,Color? color=null) => Raylib.DrawTextEx(font,text,new(x,y),size,1,color??White);
    private void Fill(float x,float y,float w,float h,Color color) => Raylib.DrawRectangleRec(new(x,y,w,h),color);
    private bool Button(string text,float x,float y,float w,float h,bool active=false,bool enabled=true)
    {
        var rect=new Rectangle(x,y,w,h); var hover=Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(),rect);
        var bg=active?new Color(37,88,77,255):hover&&enabled?new Color(42,61,80,255):new Color(27,40,57,255);
        Raylib.DrawRectangleRounded(rect,.15f,4,bg);
        Text(text,x+10,y+(h-18)/2,17,enabled?(active?Accent:White):Muted);
        return enabled && hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }
    private void Draw(float alpha)
    {
        var width=Raylib.GetScreenWidth(); var height=Raylib.GetScreenHeight(); int sideWidth=300, top=82, bottom=84;
        int vw=width-sideWidth, vh=height-top-bottom;
        if(view.Id==0 || view.Texture.Width!=vw || view.Texture.Height!=vh)
        { if(view.Id!=0)Raylib.UnloadRenderTexture(view); view=Raylib.LoadRenderTexture(vw,vh); }
        var robot=sim.Robots[selected]; var p=Vector3.Lerp(robot.PreviousPosition,robot.Position,alpha);
        var yaw=robot.Yaw+cameraYaw; var direction=new Vector3(MathF.Cos(yaw)*MathF.Cos(cameraPitch),MathF.Sin(cameraPitch),MathF.Sin(yaw)*MathF.Cos(cameraPitch));
        var eye=p+new Vector3(0,robot.Spec.Height*.91f,0);
        var camera=new Camera3D { Position=eye,Target=eye+direction,Up=Vector3.UnitY,FovY=settings.Fov,Projection=CameraProjection.Perspective };
        if(cameraMode==0) { camera.Position=p-direction*distance+new Vector3(0,2.2f,0); camera.Target=p+new Vector3(0,.4f,0)+direction*.8f; }
        if(cameraMode==2) { camera.Position=new(14,11,14); camera.Target=new(6,.15f,4); camera.FovY=43; }
        Raylib.BeginTextureMode(view); Raylib.ClearBackground(new(44,58,75,255)); Raylib.BeginMode3D(camera);
        renderer.Draw(sim,alpha,selected,cameraMode); Raylib.EndMode3D(); Raylib.EndTextureMode();
        Raylib.BeginDrawing(); Raylib.ClearBackground(Bg);
        Raylib.DrawTexturePro(view.Texture,new(0,0,vw,-vh),new(0,top,vw,vh),Vector2.Zero,0,Color.White);
        Fill(0,0,width,top,Panel); Fill(vw,top,sideWidth,vh,Panel); Fill(0,height-bottom,width,bottom,Bg);
        Text("TSIMULATOR",24,18,28); Text("CoRE-2 / 2027",25,52,15,Muted);
        var left=255f;
        Text($"RED   {sim.Alliances[0].Vp:000} VP",left,18,25,new(250,101,119,255)); Text($"{sim.Alliances[0].Rp} RP",left,51,15,Muted);
        Text($"BLUE   {sim.Alliances[1].Vp:000} VP",left+230,18,25,new(103,169,255,255)); Text($"{sim.Alliances[1].Rp} RP",left+230,51,15,Muted);
        var remaining=Math.Max(0,sim.Rules.RoundSeconds-sim.Elapsed);
        Text($"{(int)remaining/60:00}:{(int)remaining%60:00}",vw-150,17,34);
        Text(paused?"PAUSED":recorder is not null?"REC / 120 Hz":"LIVE / 120 Hz",vw-150,55,13,recorder is not null?new(250,101,119,255):Accent);
        if(Button("設定  F1",width-155,22,125,37)) { menu=true; draft=scenario; }
        Text(cameraMode==1?"ロボット視点":cameraMode==0?"追従視点 / 練習補助":"フィールド / 練習補助",20,top+16,17);
        Text(renderer.OfficialLoaded?"CAD / V27.2.0":"寸法モデル / CAD取得は設定から",20,top+40,14,new(227,236,245,255));
        if(cameraMode==1)
        {
            int cx=vw/2, cy=top+vh/2; Raylib.DrawLine(cx-13,cy,cx+13,cy,Accent); Raylib.DrawLine(cx,cy-13,cx,cy+13,Accent);
            Text("CAM / GIMBAL",cx+20,cy+12,12,Accent);
        }
        if(paused || sim.Finished)
        {
            Fill(vw/2-235,top+vh/2-46,470,92,new(12,19,30,220));
            Text(sim.Finished?"ROUND COMPLETE":"一時停止",vw/2-210,top+vh/2-25,27,Accent);
            Text(sim.Finished?sim.Result().Reason:"P で再開",vw/2-210,top+vh/2+13,15);
        }
        DrawSidebar(vw,height);
        Text($"{settings.Keys["Forward"]}{settings.Keys["Left"]}{settings.Keys["Backward"]}{settings.Keys["Right"]}  移動    {settings.Keys["TurnLeft"]}/{settings.Keys["TurnRight"]}  旋回    {settings.Keys["Fire"]}  射撃    {settings.Keys["Interact"]}  回収・設置",22,height-69,17);
        Text($"{settings.Keys["CameraMode"]}  視点    {settings.Keys["SwitchRobot"]}  ロボット    {settings.Keys["ArmUp"]}/{settings.Keys["ArmDown"]}  アーム    {settings.Keys["Record"]}  記録    {settings.Keys["Pause"]}  停止",22,height-42,14,Muted);
        Text($"{Raylib.GetFPS()} FPS  /  {timeScale:0.0}x",width-190,height-66,15,Accent);
        Text(Clip(message,Math.Max(30,(width-50)/12)),22,height-20,12,Muted);
        if(menu) DrawSettings(width,height);
        Raylib.EndDrawing();
    }
    private static string Clip(string text,int count) => text.Length>count?text[..count]+"…":text;
    private void DrawSidebar(int x,int height)
    {
        var r=sim.Robots[selected]; var team=sim.Team(r.Side);
        Text("操縦ロボット",x+20,99,14,Muted); Text($"{r.Side.ToString().ToUpperInvariant()} / {r.Id+1:00}",x+20,121,24);
        Text($"HP {r.Hp}/{sim.Rules.InitialHp}     弾 {r.Ammo}",x+20,156,17);
        Fill(x+20,184,260,5,new(39,52,70,255)); Fill(x+20,184,260*(float)r.Hp/sim.Rules.InitialHp,5,Accent);
        Text(r.Stopped?"非常停止 / Rでラウンド再開":!r.Alive?$"復活まで {Math.Max(0,(r.RespawnAt-sim.Tick)/(float)sim.Rules.TickRate):0.0} 秒":r.PitUntil>sim.Tick?$"補給 {((r.PitUntil-sim.Tick)/(float)sim.Rules.TickRate):0.0} 秒":$"アーム {r.ArmHeight:0.000} m / {((r.ContainerId.HasValue)?"コンテナ有":"コンテナ無")}",x+20,201,14,Muted);
        Text("設置先 / スポット番号",x+20,234,14,Muted);
        for(var cell=0;cell<9;cell++)
        {
            var px=x+20+(cell%3)*87; var py=261+(2-cell/3)*30;
            var filled=(team.SpotMask&(1<<cell))!=0;
            if(Button($"{cell+1}{(filled?" / OK":"")}",px,py,80,25,selectedCell==cell)) selectedCell=cell;
        }
        Text("総大将 / スキル",x+20,360,14,Muted);
        for(var i=0;i<Skills.Length;i++)
        {
            var s=Skills[i]; var exempt=s.Skill is Skill.PitIn or Skill.Supply;
            bool enabled=team.Rp>=s.Cost && (exempt || sim.Tick>=team.SkillReadyAt);
            if(Button($"{settings.Keys[s.Skill.ToString()]}  {s.Name}    {s.Cost} RP",x+20,387+i*33,260,28,team.Effect==s.Skill && sim.Tick<team.EffectUntil,enabled))pendingSkill=s.Skill;
        }
        Text($"スキル待機 {Math.Max(0,(team.SkillReadyAt-sim.Tick)/(float)sim.Rules.TickRate):0.0}s",x+20,691,13,Muted);
        if(settings.ShowMinimap && height>=860)
        {
            float mx=x+20,my=720,mw=260,mh=120;
            Fill(mx,my,mw,mh,new(28,43,60,255));
            foreach(var o in sim.Obstacles)
            { var c=o.Center; Raylib.DrawCircle((int)(mx+c.X/12*mw),(int)(my+c.Z/8*mh),5,new(114,135,156,255)); }
            foreach(var rb in sim.Robots)
            {
                var color=rb.Side==Side.Red?new Color(250,101,119,255):new Color(103,169,255,255);
                var pos=new Vector2(mx+rb.Position.X/12*mw,my+rb.Position.Z/8*mh);
                Raylib.DrawCircleV(pos,rb.Id==selected?5:3,color); Raylib.DrawLineV(pos,pos+new Vector2(rb.Forward.X,rb.Forward.Z)*10,color);
            }
            Text("MAP / 練習補助",mx+7,my+5,11,Muted);
        }
    }
    private void DrawSettings(int width,int height)
    {
        Fill(0,0,width,height,new(4,10,18,205));
        var x=(width-1000)/2; var y=(height-700)/2;
        Raylib.DrawRectangleRounded(new(x,y,1000,700),.025f,8,Panel);
        Text("設定 / PRACTICE WORKSPACE",x+26,y+20,25);
        if(Button("閉じる",x+866,y+17,105,35))menu=false;
        string[] tabs=["キー操作","ロボット・物理","シナリオ","CAD・記録"];
        for(var i=0;i<4;i++)if(Button(tabs[i],x+26+i*230,y+70,216,37,settingsTab==i)){settingsTab=i;binding=null;}
        if(settingsTab==0)DrawBindings(x,y);
        if(settingsTab==1)DrawParameters(x,y);
        if(settingsTab==2)DrawScenarios(x,y);
        if(settingsTab==3)DrawAssets(x,y);
        if(Button("設定を保存",x+26,y+639,180,38,true))
        {
            try { settings.Validate(); JsonFiles.Save(Paths.Settings,settings); Raylib.SetTargetFPS(settings.TargetFps);message="設定を保存しました"; }
            catch(Exception e) when(e is IOException){message=e.Message;}
        }
        if(Button("変更を適用・ラウンド再開",x+230,y+639,330,38))
        {
            try { Reset(draft);menu=false;message="設定を適用しました"; }
            catch(Exception e) when(e is IOException){message=e.Message;}
        }
        Text(Clip(message,48),x+26,y+609,14,Muted);
    }
    private void DrawBindings(int x,int y)
    {
        Text("項目をクリックし、割り当てるキーを押してください。重複するキーは入れ替えます。",x+26,y+124,15,Muted);
        var pairs=settings.Keys.ToArray();
        for(var i=0;i<pairs.Length;i++)
        {
            int col=i/16,row=i%16; var px=x+26+col*478;var py=y+159+row*26;
            var key=pairs[i].Key; var label=Labels.GetValueOrDefault(key,Skills.FirstOrDefault(s=>s.Skill.ToString()==key).Name??key);
            Text(label,px,py+4,14);
            if(Button(binding==key?"キー入力待機 / Esc":pairs[i].Value,px+250,py,195,23,binding==key))
            { while(Raylib.GetKeyPressed()!=0){} binding=key; }
        }
    }
    private void Number(string name,float value,float step,float minimum,float maximum,int x,int y,Action<float> update,string format="0.00")
    {
        Text(name,x,y+5,15);Text(value.ToString(format,System.Globalization.CultureInfo.InvariantCulture),x+245,y+5,16,Accent);
        if(Button("-",x+336,y,36,28))update(Math.Clamp(value-step,minimum,maximum));
        if(Button("+",x+380,y,36,28))update(Math.Clamp(value+step,minimum,maximum));
    }
    private void DrawParameters(int x,int y)
    {
        Text("競技規定の制限は適用時に確認します。空力・走行値は実測に合わせて調整してください。",x+26,y+124,15,Muted);
        int a=x+26,b=x+516,c=y+166;
        Number("走行速度 m/s",draft.Robot.MaxSpeed,.25f,.25f,15,a,c,v=>draft=draft with{Robot=draft.Robot with{MaxSpeed=v}});
        Number("加速度 m/s²",draft.Robot.Acceleration,.5f,.5f,20,a,c+40,v=>draft=draft with{Robot=draft.Robot with{Acceleration=v}});
        Number("旋回 rad/s",draft.Robot.TurnSpeed,.25f,.25f,8,a,c+80,v=>draft=draft with{Robot=draft.Robot with{TurnSpeed=v}});
        Number("射出速度 m/s",draft.Robot.ShotSpeed,.25f,1,20,a,c+120,v=>draft=draft with{Robot=draft.Robot with{ShotSpeed=v}});
        Number("射出高さ m",draft.Robot.ShotHeight,.025f,.05f,1,a,c+160,v=>draft=draft with{Robot=draft.Robot with{ShotHeight=v}});
        Number("幅 m",draft.Robot.Width,.05f,.3f,1.2f,a,c+200,v=>draft=draft with{Robot=draft.Robot with{Width=v}});
        Number("長さ m",draft.Robot.Depth,.05f,.3f,1.2f,a,c+240,v=>draft=draft with{Robot=draft.Robot with{Depth=v}});
        Number("高さ m",draft.Robot.Height,.05f,.3f,1.2f,a,c+280,v=>draft=draft with{Robot=draft.Robot with{Height=v}});
        Number("マガジン容量",draft.Robot.MagazineCapacity,5,5,100,a,c+320,v=>draft=draft with{Robot=draft.Robot with{MagazineCapacity=(int)v}},"0");
        Number("射撃間隔 s",draft.Robot.ShotInterval,.02f,.05f,2,a,c+360,v=>draft=draft with{Robot=draft.Robot with{ShotInterval=v}});
        Number("空気抵抗係数",draft.Physics.Drag,.01f,0,1,b,c,v=>draft=draft with{Physics=draft.Physics with{Drag=v}},"0.000");
        Number("揚力係数",draft.Physics.Lift,.005f,0,.5f,b,c+40,v=>draft=draft with{Physics=draft.Physics with{Lift=v}},"0.000");
        Number("風 X m/s",draft.Physics.WindX,.5f,-10,10,b,c+80,v=>draft=draft with{Physics=draft.Physics with{WindX=v}});
        Number("風 Z m/s",draft.Physics.WindZ,.5f,-10,10,b,c+120,v=>draft=draft with{Physics=draft.Physics with{WindZ=v}});
        Number("カメラ FOV",settings.Fov,5,35,110,b,c+160,v=>settings.Fov=v,"0");
        Number("カメラ感度",settings.CameraSensitivity,.1f,.1f,5,b,c+200,v=>settings.CameraSensitivity=v);
        Number("FPS上限",settings.TargetFps,30,30,360,b,c+240,v=>settings.TargetFps=(int)v,"0");
        Number("再生速度",timeScale,.25f,.25f,4,b,c+280,v=>timeScale=v);
        if(Button("走行方式: "+(draft.Robot.Holonomic?"全方向":"差動"),b,c+330,416,32))draft=draft with{Robot=draft.Robot with{Holonomic=!draft.Robot.Holonomic}};
        if(Button("ミニマップ: "+(settings.ShowMinimap?"有効":"無効"),b,c+371,416,32))settings.ShowMinimap=!settings.ShowMinimap;
    }
    private void DrawScenarios(int x,int y)
    {
        Text("CoRE-2 / V27.2.0 / 12m × 8m / 120 Hz",x+26,y+136,22,Accent);
        Text("プリセットを選択後、適用するとラウンドを再開します。",x+26,y+181,16,Muted);
        if(Button("準決勝 / 2対2 / 200枚",x+26,y+220,450,46,draft.RobotsPerSide==2&&!draft.Sandbox))draft=new();
        if(Button("決勝 / 3対3 / 300枚",x+516,y+220,450,46,draft.RobotsPerSide==3&&!draft.Sandbox))draft=new(){Name="CoRE-2 2027 / final",RobotsPerSide=3,Rules=new(){InitialDiscs=300}};
        if(Button("自由練習 / 制限を変更可能",x+26,y+283,450,46,draft.Sandbox))draft=draft with{Name="Free practice",Sandbox=true};
        if(Button("対戦ボット: "+(draft.BotsEnabled?"有効":"無効"),x+516,y+283,450,46))draft=draft with{BotsEnabled=!draft.BotsEnabled};
        if(Button("初期コンテナ: "+(draft.PreloadContainers?"有効":"無効"),x+26,y+346,450,46))draft=draft with{PreloadContainers=!draft.PreloadContainers};
        if(Button("無限弾: "+(draft.InfiniteAmmo?"有効":"無効"),x+516,y+346,450,46,enabled:draft.Sandbox))draft=draft with{InfiniteAmmo=!draft.InfiniteAmmo};
        Number("RPボーナス",draft.RpBonus,50,0,1000,x+26,y+422,v=>draft=draft with{RpBonus=(int)v},"0");
        if(Button("シナリオをJSONに保存",x+26,y+481,450,40))
        { var path=Path.Combine(Paths.User,"scenario.json");JsonFiles.Save(path,draft);scenarioPath=path;message="保存: "+path; }
        if(Button("保存したシナリオを読込",x+516,y+481,450,40,enabled:File.Exists(Path.Combine(Paths.User,"scenario.json"))))
        { try { scenarioPath=Path.Combine(Paths.User,"scenario.json");draft=JsonFiles.Load<Scenario>(scenarioPath);draft.Validate();message="シナリオを読込しました"; }catch(Exception e)when(e is IOException or System.Text.Json.JsonException){message=e.Message;} }
        Text("JSONで障害物・機体OBJ/GLB・物理・競技値を編集し、F5で再読込できます。",x+26,y+548,16,Muted);
    }
    private void DrawAssets(int x,int y)
    {
        Text("公式CAD / CoRE-2ルールナビ",x+26,y+138,22,Accent);
        Text("CAD由来のフィールドと120mmコンテナを、公開ページからローカルに取得します。",x+26,y+181,16);
        Text("取得元・版・SHA-256を保存します。取得中も画面は操作できます。",x+26,y+213,16,Muted);
        if(Button(download is null?"公式CADを取得 / 更新":"CAD取得中…",x+26,y+253,450,42,enabled:download is null))download=OfficialAssets.FetchAsync(Paths.Models);
        Text(renderer.OfficialLoaded?"状態: 公式モデル読込完了":"状態: 寸法に基づくモデル",x+516,y+264,17);
        Text("source / core.scramble-robot.org/rule/core-2-rulenavi/",x+26,y+317,14,Muted);
        Text("記録 / 検証",x+26,y+365,22,Accent);
        Text("F9: 新しいラウンドから入力を記録。再度F9で保存。CLIで再生・一致を検証します。",x+26,y+409,16);
        Text("dotnet run --project src/TSimulator.Cli -- replay FILE.jsonl",x+26,y+445,16,Muted);
        Text("保存先: "+Clip(Paths.User,75),x+26,y+481,14,Muted);
        Text("許可に基づき公式CADを同梱しています。更新時は取得元・版を確認してください。",x+26,y+531,15,Muted);
        Text("空力・接触は近似です。実機の射出試験で係数を校正してください。",x+26,y+562,15,Muted);
    }
}
