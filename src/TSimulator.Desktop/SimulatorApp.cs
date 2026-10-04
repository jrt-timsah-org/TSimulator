using System.Diagnostics;
using System.Numerics;
using Raylib_cs;
using TSimulator.Core;
namespace TSimulator.Desktop;

public sealed partial class SimulatorApp
{
    private static readonly Color Bg = new(12, 19, 30, 255), Panel = new(19, 29, 43, 255), Muted = new(135, 157, 180, 255), White = new(229, 239, 246, 255), Accent = new(106, 237, 183, 255);
    private static readonly (Skill Skill, string Name)[] Skills = [(Skill.PitIn,"ピットイン"),(Skill.Supply,"サプライ"),
        (Skill.Healing1,"回復 Lv.1"),(Skill.Healing2,"回復 Lv.2"),(Skill.Boost1,"ブースト Lv.1"),
        (Skill.Boost2,"ブースト Lv.2"),(Skill.Barrier1,"バリア Lv.1"),(Skill.Barrier2,"バリア Lv.2"),(Skill.Regenerate,"リジェネ")];
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
    private readonly string? automationPath;
    private bool noOfficial;
    private Scenario? validatedDraft;
    private string? draftValidationError;
    public SimulatorApp(string[] args)
    {
        this.args = args;
        string? Option(string name) { var i = Array.IndexOf(args,name); return i>=0 && i+1<args.Length ? args[i+1] : null; }
        if(Option("--user-data") is string userDirectory)Paths.SetUserDirectory(userDirectory);
        scenarioPath = Option("--scenario");
        scenario = scenarioPath is not null ? JsonFiles.Load<Scenario>(scenarioPath) : new Scenario();
        settings = File.Exists(Paths.Settings) ? JsonFiles.Load<AppSettings>(Paths.Settings) : new();
        settings.Validate(); draft = scenario; sim = new(scenario); commands = new RobotCommand[sim.Robots.Count];
        smokeFrames = int.TryParse(Option("--smoke"),out var frames) ? Math.Clamp(frames,1,10000) : 0;
        screenshot = Option("--screenshot"); noOfficial = args.Contains("--no-official");
        automationPath=Option("--automation");
        if(automationPath is not null && smokeFrames==0)throw new ArgumentException("--automation requires --smoke for a bounded run.");
        if (smokeFrames > 0) cameraMode = 2;
        menu = args.Contains("--settings");
        LoadBuilderCatalog();
        if (args.Contains("--builder")) { menu=true; settingsTab=1; }
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
    public unsafe int Run()
    {
        if (args.Contains("--help")) { Console.WriteLine("TSimulator.Desktop [--scenario FILE] [--settings | --builder] [--no-official] [--user-data DIRECTORY] [--smoke FRAMES --screenshot PNG --automation FILE.rae]"); return 0; }
        if (args.Contains("--native-check"))
        {
            Raylib.SetRandomSeed(42);
            Console.WriteLine($"NATIVE OK: {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}, sample={Raylib.GetRandomValue(1,1)}");
            return 0;
        }
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | (smokeFrames>0 ? 0 : ConfigFlags.Msaa4xHint));
        Raylib.InitWindow(1440,900,"TSimulator | CoRE-2 2027");
        if (!Raylib.IsWindowReady())
        {
            Console.Error.WriteLine("Could not create an OpenGL 3.3 window. Check graphics drivers and run from a graphical desktop.");
            return 2;
        }
        Raylib.SetWindowMinSize(1100,760); Raylib.SetExitKey(KeyboardKey.Null); Raylib.SetTargetFPS(settings.TargetFps);
        var glyphs = UiGlyphs();
        var fontPath = Path.Combine(Paths.Content,"assets","fonts","NotoSansJP-Regular.otf");
        font = File.Exists(fontPath) ? Raylib.LoadFontEx(fontPath,32,glyphs,glyphs.Length) : Raylib.GetFontDefault();
        Raylib.SetTextureFilter(font.Texture,TextureFilter.Bilinear);
        renderer = new(!noOfficial);
        AutomationEvent[] automation=[];int automationIndex=0;
        int frame = 0;
        try
        {
            if(smokeFrames>0)Raylib.SetMousePosition(4,4);
            if(automationPath is not null)
            {
                automation=UiAutomation.Load(automationPath,smokeFrames);
            }
            while (!quit && !Raylib.WindowShouldClose())
            {
                while(automationIndex<automation.Length && automation[automationIndex].Frame==frame)
                    Raylib.PlayAutomationEvent(automation[automationIndex++]);
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
            if (builderView.Id!=0) Raylib.UnloadRenderTexture(builderView);
            if (File.Exists(fontPath)) Raylib.UnloadFont(font);
            Raylib.CloseWindow();
        }
        return 0;
    }
    private void HandleInput(float dt)
    {
        if (!Raylib.IsWindowFocused() && smokeFrames==0) { ClearPendingActions(); return; }
        if (modulePicker is not null && Raylib.IsKeyPressed(KeyboardKey.Escape)) { modulePicker=null; return; }
        if (builderNameEditing && menu) return;
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
        if (Pressed("Settings") || Raylib.IsKeyPressed(KeyboardKey.Escape))
        { menu=!menu; if(menu) { draft=scenario; builderInitialized=false; } ClearPendingActions(); }
        if (menu) { ClearPendingActions(); return; }
        if (Pressed("Pause")) { paused=!paused; ClearPendingActions(); }
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
        if (Pressed("SwitchRobot")) { ClearPendingActions(); selected=(selected+1)%sim.Robots.Count; cameraYaw=0; cameraPitch=-.03f; }
        if (Pressed("CameraMode")) cameraMode=(cameraMode+1)%3;
        if (!paused && !sim.Finished)
        {
            if (Pressed("Interact")) pendingInteract=true;
            if (Pressed("Drop")) pendingDrop=true;
            if (Pressed("EmergencyStop")) pendingStop=true;
            for(var i=0;i<9;i++) if(Pressed(Skills[i].Skill.ToString())) pendingSkill=Skills[i].Skill;
        }
        else ClearPendingActions();
        cameraYaw=Math.Clamp(cameraYaw+Axis("CameraRight","CameraLeft")*dt*settings.CameraSensitivity,-2.8f,2.8f);
        cameraPitch=Math.Clamp(cameraPitch+Axis("CameraUp","CameraDown")*dt*settings.CameraSensitivity,-1.1f,.7f);
        if (Raylib.IsMouseButtonDown(MouseButton.Right))
        {
            var mouse=Raylib.GetMouseDelta(); cameraYaw=Math.Clamp(cameraYaw+mouse.X*.003f,-2.8f,2.8f); cameraPitch=Math.Clamp(cameraPitch-mouse.Y*.003f,-1.1f,.7f);
        }
        if (Raylib.GetMousePosition().X<Raylib.GetScreenWidth()-SidebarWidth)
            distance=Math.Clamp(distance-Raylib.GetMouseWheelMove()*.4f,1.5f,12);
    }
    private void ClearPendingActions() { pendingInteract=pendingDrop=pendingStop=false; pendingSkill=Skill.None; }
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
        selected=Math.Min(selected,sim.Robots.Count-1); accumulator=0; paused=false; ClearPendingActions();
        cameraYaw=0; cameraPitch=-.03f;
    }
    private void Text(string text,float x,float y,float size=18,Color? color=null) => Raylib.DrawTextEx(font,text,new(x,y),size,1,color??White);
    private void Fill(float x,float y,float w,float h,Color color) => Raylib.DrawRectangleRec(new(x,y,w,h),color);
    private bool Button(string text,float x,float y,float w,float h,bool active=false,bool enabled=true,string? tooltip=null)
    {
        var rect=new Rectangle(x,y,w,h); var hover=Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(),rect);
        hover &= !menu || drawingSettings;
        hover &= hitClip is null || Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(),hitClip.Value);
        hover &= modulePicker is null || !drawingSettings || builderPickerDrawing;
        if(hover && tooltip is not null) hoverTip=tooltip;
        if(hover && !enabled && Raylib.IsMouseButtonPressed(MouseButton.Left) && tooltip is not null)message=tooltip;
        var bg=active?new Color(37,88,77,255):hover&&enabled?new Color(42,61,80,255):new Color(27,40,57,255);
        Raylib.DrawRectangleRounded(rect,.15f,4,bg);
        Text(text,x+10,y+(h-18)/2,17,enabled?(active?Accent:White):Muted);
        return enabled && hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }
    private void Draw(float alpha)
    {
        var width=Raylib.GetScreenWidth(); var height=Raylib.GetScreenHeight(); int sideWidth=300, top=82, bottom=84;
        sideWidth=SidebarWidth; hoverTip=null;
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
        var left=210f;
        Text($"RED  {sim.Alliances[0].Vp:000} VP",left,18,22,new(250,101,119,255)); Text($"{sim.Alliances[0].Rp} RP / 予備 {sim.Alliances[0].Reserve}",left,51,14,Muted);
        Text($"BLUE  {sim.Alliances[1].Vp:000} VP",left+185,18,22,new(103,169,255,255)); Text($"{sim.Alliances[1].Rp} RP / 予備 {sim.Alliances[1].Reserve}",left+185,51,14,Muted);
        var remaining=Math.Max(0,sim.Rules.RoundSeconds-sim.Elapsed);
        Text($"{(int)remaining/60:00}:{(int)remaining%60:00}",vw-150,17,34);
        Text(SessionLabel,vw-150,55,13,SessionRunning?Accent:Muted);
        if(Button($"設定  {KeyName("Settings")}",width-155,22,125,37)) { menu=true; draft=scenario; builderInitialized=false; ClearPendingActions(); }
        Text(cameraMode==1?"ロボット視点":cameraMode==0?"追従視点 / 練習補助":"フィールド / 練習補助",20,top+16,17);
        Text(renderer.OfficialLoaded?"CAD / V27.2.0":"寸法モデル / CAD取得は設定から",20,top+40,14,new(227,236,245,255));
        if(cameraMode==1)
        {
            DrawAim(camera,vw,vh,top);
        }
        if(paused || sim.Finished)
        {
            Fill(vw/2-235,top+vh/2-46,470,92,new(12,19,30,220));
            Text(sim.Finished?"ROUND COMPLETE":"一時停止",vw/2-210,top+vh/2-25,27,Accent);
            Text(sim.Finished?sim.Result().Reason:$"{KeyName("Pause")} で再開",vw/2-210,top+vh/2+13,15);
        }
        DrawSidebar(vw,height);
        Text($"{settings.Keys["Forward"]}{settings.Keys["Left"]}{settings.Keys["Backward"]}{settings.Keys["Right"]}  移動    {settings.Keys["TurnLeft"]}/{settings.Keys["TurnRight"]}  旋回    {settings.Keys["Fire"]}  射撃    {settings.Keys["Interact"]}  回収・設置",22,height-69,17);
        Text($"{settings.Keys["CameraMode"]}  視点    {settings.Keys["SwitchRobot"]}  ロボット    {settings.Keys["ArmUp"]}/{settings.Keys["ArmDown"]}  アーム    {settings.Keys["Record"]}  記録    {settings.Keys["Pause"]}  停止",22,height-42,14,Muted);
        Text($"{Raylib.GetFPS()} FPS  /  {timeScale:0.0}x",width-190,height-66,15,Accent);
        Text(Clip(message,Math.Max(30,(width-50)/12)),22,height-20,12,Muted);
        if(menu) { drawingSettings=true; DrawSettings(width,height); drawingSettings=false; }
        DrawTooltip(width,height);
        Raylib.EndDrawing();
    }
    private static string Clip(string text,int count) => text.Length>count?text[..count]+"…":text;
    private void DrawSettings(int width,int height)
    {
        Fill(0,0,width,height,new(4,10,18,205));
        var x=(width-1000)/2; var y=(height-700)/2;
        Raylib.DrawRectangleRounded(new(x,y,1000,700),.025f,8,Panel);
        Text("設定 / PRACTICE WORKSPACE",x+26,y+20,25);
        if(Button("閉じる",x+866,y+17,105,35))menu=false;
        string[] tabs=["キー操作","機体ビルダー","性能・物理","シナリオ","CAD・記録"];
        for(var i=0;i<5;i++)if(Button(tabs[i],x+26+i*190,y+70,180,37,settingsTab==i)){settingsTab=i;binding=null;builderNameEditing=false;if(i==1)builderInitialized=false;}
        if(settingsTab==0)DrawBindings(x,y);
        if(settingsTab==1)DrawBuilder(x,y);
        if(settingsTab==2)DrawParameters(x,y);
        if(settingsTab==3)DrawScenarios(x,y);
        if(settingsTab==4)DrawAssets(x,y);
        if(Button("設定を保存",x+26,y+639,180,38,true))
        {
            try { settings.Validate(); JsonFiles.Save(Paths.Settings,settings); Raylib.SetTargetFPS(settings.TargetFps);message="設定を保存しました"; }
            catch(Exception e) when(e is IOException){message=e.Message;}
        }
        if(!ReferenceEquals(validatedDraft,draft))
        {
            validatedDraft=draft;
            try { draft.Validate();draftValidationError=null; }
            catch(IOException e){draftValidationError=e.Message;}
        }
        if(Button("変更を適用・ラウンド再開",x+230,y+639,330,38,enabled:draftValidationError is null,
            tooltip:draftValidationError is null?"編集した機体・シナリオを適用し、新しいラウンドを開始します。":"適用できません。競技規定・対象機体・数値を確認してください。\n"+draftValidationError))
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
        var previous=draft.Robot;
        if(Button("-",x+336,y,36,28))update(Math.Clamp(value-step,minimum,maximum));
        if(Button("+",x+380,y,36,28))update(Math.Clamp(value+step,minimum,maximum));
        if(draft.Robot!=previous){draft=draft with { Robot=draft.Robot with { Assembly=null } };builderInitialized=false;}
    }
    private void DrawParameters(int x,int y)
    {
        if(Button(builderAll?"編集対象: 全機体に共通":$"編集対象: 選択機体 {selected+1:00}",x+26,y+124,416,30,
            tooltip:"全機体を選ぶと機体別設定を解除し、共通の機体値を使います。"))
        { builderAll=!builderAll;if(builderAll)draft=draft with { RobotOverrides=[] };builderInitialized=false; }
        Text("機体値は編集対象へ、物理・画面値は全体へ適用。",x+516,y+130,14,Muted);
        int a=x+26,b=x+516,c=y+166;
        Number("走行速度 m/s",EditableRobot.MaxSpeed,.25f,.25f,15,a,c,v=>EditRobot(r=>r with{MaxSpeed=v}));
        Number("加速度 m/s²",EditableRobot.Acceleration,.5f,.5f,20,a,c+40,v=>EditRobot(r=>r with{Acceleration=v}));
        Number("旋回 rad/s",EditableRobot.TurnSpeed,.25f,.25f,8,a,c+80,v=>EditRobot(r=>r with{TurnSpeed=v}));
        Number("射出速度 m/s",EditableRobot.ShotSpeed,.25f,1,20,a,c+120,v=>EditRobot(r=>r with{ShotSpeed=v}));
        Number("射出高さ m",EditableRobot.ShotHeight,.025f,.05f,1,a,c+160,v=>EditRobot(r=>r with{ShotHeight=v}));
        Number("幅 m",EditableRobot.Width,.05f,.3f,1.2f,a,c+200,v=>EditRobot(r=>r with{Width=v}));
        Number("長さ m",EditableRobot.Depth,.05f,.3f,1.2f,a,c+240,v=>EditRobot(r=>r with{Depth=v}));
        Number("高さ m",EditableRobot.Height,.05f,.3f,1.2f,a,c+280,v=>EditRobot(r=>r with{Height=v}));
        Number("マガジン容量",EditableRobot.MagazineCapacity,5,5,100,a,c+320,v=>EditRobot(r=>r with{MagazineCapacity=(int)v}),"0");
        Number("射撃間隔 s",EditableRobot.ShotInterval,.02f,.05f,2,a,c+360,v=>EditRobot(r=>r with{ShotInterval=v}));
        Number("空気抵抗係数",draft.Physics.Drag,.01f,0,1,b,c,v=>draft=draft with{Physics=draft.Physics with{Drag=v}},"0.000");
        Number("揚力係数",draft.Physics.Lift,.005f,0,.5f,b,c+40,v=>draft=draft with{Physics=draft.Physics with{Lift=v}},"0.000");
        Number("風 X m/s",draft.Physics.WindX,.5f,-10,10,b,c+80,v=>draft=draft with{Physics=draft.Physics with{WindX=v}});
        Number("風 Z m/s",draft.Physics.WindZ,.5f,-10,10,b,c+120,v=>draft=draft with{Physics=draft.Physics with{WindZ=v}});
        Number("カメラ FOV",settings.Fov,5,35,110,b,c+160,v=>settings.Fov=v,"0");
        Number("カメラ感度",settings.CameraSensitivity,.1f,.1f,5,b,c+200,v=>settings.CameraSensitivity=v);
        Number("FPS上限",settings.TargetFps,30,30,360,b,c+240,v=>settings.TargetFps=(int)v,"0");
        Number("再生速度",timeScale,.25f,.25f,4,b,c+280,v=>timeScale=v);
        if(Button("走行方式: "+(EditableRobot.Holonomic?"全方向":"差動"),b,c+330,416,32))EditRobot(r=>r with{Holonomic=!EditableRobot.Holonomic});
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
