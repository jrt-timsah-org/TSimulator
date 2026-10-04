using System.Numerics;
using Raylib_cs;
using TSimulator.Core;

namespace TSimulator.Desktop;

public sealed partial class SimulatorApp
{
    private const int SidebarWidth=350;
    private static readonly Color Warning=new(255,197,105,255), Danger=new(250,101,119,255);
    private bool drawingSettings;
    private string? hoverTip;
    private Rectangle? hitClip;
    private int sidebarTab;
    private float sidebarScroll;
    private float AimYaw => cameraMode==1?Math.Clamp(cameraYaw,-.8f,.8f):0;
    private float AimPitch => cameraMode==1?Math.Clamp(cameraPitch,-.3f,.5f):0;
    private bool AimLimited => cameraMode==1 && (Math.Abs(cameraYaw)>.8f || cameraPitch is < -.3f or > .5f);
    private bool SessionRunning => !paused && !menu && !sim.Finished && (Raylib.IsWindowFocused() || smokeFrames>0);
    private string SessionLabel => sim.Finished?"ラウンド終了":menu?"設定中 / 停止":paused?"一時停止":!Raylib.IsWindowFocused()&&smokeFrames==0?"非フォーカス / 停止":$"{(recorder is null?"LIVE":"REC")} / {sim.Rules.TickRate} Hz";
    private string KeyName(string action) => settings.Keys[action] switch
    { "One"=>"1","Two"=>"2","Three"=>"3","Four"=>"4","Five"=>"5","Six"=>"6","Seven"=>"7","Eight"=>"8","Nine"=>"9",
        "Up"=>"↑","Down"=>"↓","Left"=>"←","Right"=>"→",var key=>key };
    private static string SkillName(Skill skill) => Skills.FirstOrDefault(s=>s.Skill==skill).Name??"なし";

    private string AvailabilityText(ActionAvailability status) => status.Reason switch
    {
        ActionBlockReason.Ready=>"使用可能",
        ActionBlockReason.RoundFinished=>"ラウンド終了",
        ActionBlockReason.InvalidTarget=>"対象の機体・スポットが無効",
        ActionBlockReason.InvalidCommand=>"無効な操作",
        ActionBlockReason.EmergencyStopped=>$"非常停止 / {KeyName("Reset")} で新ラウンド",
        ActionBlockReason.KnockedOut=>$"撃破 / 復活まで {sim.Seconds(status.RemainingTicks):0.0} 秒",
        ActionBlockReason.PitRefilling=>$"補給中 / 残り {sim.Seconds(status.RemainingTicks):0.0} 秒",
        ActionBlockReason.OutOfAmmo=>"弾切れ / ピットインで補給",
        ActionBlockReason.ShotCooldown=>$"次の射撃まで {sim.Seconds(status.RemainingTicks):0.00} 秒",
        ActionBlockReason.ProjectileCapacity=>"飛行中ディスク数が上限",
        ActionBlockReason.MuzzleObstructed=>"銃口が壁・障害物で塞がれています",
        ActionBlockReason.EffectActive=>$"効果中 / 使用まで {sim.Seconds(status.RemainingTicks):0.0} 秒",
        ActionBlockReason.SkillCooldown=>$"共通待機 {sim.Seconds(status.RemainingTicks):0.0} 秒",
        ActionBlockReason.InsufficientRp=>$"RP不足 / あと {status.MissingRp} RP",
        ActionBlockReason.PitAlreadyAuthorized=>"入場許可済 / 自陣の補給ゾーンへ",
        ActionBlockReason.PitVisitInProgress=>status.RemainingTicks>0?$"補給中 {sim.Seconds(status.RemainingTicks):0.0} 秒":"補給完了 / エリアから退出してください",
        ActionBlockReason.NoKnockedOutRobot=>"復活できる味方がいません",
        ActionBlockReason.SpotOccupied=>"選択スポットは設置済み",
        ActionBlockReason.ArmHeightMismatch=>"アーム高さを設置段に合わせてください",
        ActionBlockReason.OutOfReach=>"選択スポットに近づいてください",
        ActionBlockReason.NoContainerNearby=>"回収できるコンテナが範囲内にありません",
        ActionBlockReason.PanelHitCooldown=>$"パネル受付待機 {sim.Seconds(status.RemainingTicks):0.00}秒 / 最大3Hz",
        ActionBlockReason.RespawnInvulnerability=>"対象が復活無敵中",
        ActionBlockReason.BarrierInvulnerability=>"対象にバリア Lv.2が発動中",
        _=>status.Reason.ToString()
    };

    private static string SkillDescription(Skill skill) => skill switch
    {
        Skill.PitIn=>"選択機体の入場を1回許可。機体全体が自陣補給ゾーンに入ると20秒停止して予備ディスクから装填します。共通待機の対象外。",
        Skill.Supply=>"同盟の予備ディスクを100枚追加します。マガジンへの装填にはピットインが必要です。共通待機の対象外。",
        Skill.Healing1=>"生存している味方全機のHPを30回復。撃破機体は復活しません。即時効果の後に共通待機。",
        Skill.Healing2=>"生存している味方全機のHPを全回復。撃破機体は復活しません。即時効果の後に共通待機。",
        Skill.Boost1 or Skill.Boost2=>"味方全機のダメージを2倍にします。効果中は共通スキルを重ねて使えません。",
        Skill.Barrier1=>"味方全機が受けるダメージを半減します。",
        Skill.Barrier2=>"味方全機を無敵にします。",
        Skill.Regenerate=>"撃破された味方1台を全HPで復活。選択機体が対象外なら、復活可能な味方を自動選択します。",
        _=>""
    };

    private void Progress(float x,float y,float width,float value,Color color)
    { Fill(x,y,width,4,new(39,52,70,255));Fill(x,y,width*Math.Clamp(value,0,1),4,color); }
    private void FittedText(string text,float x,float y,float width,float size=15,Color? color=null)
    {
        while(text.Length>1 && Raylib.MeasureTextEx(font,text,size,1).X>width)text=text[..^1];
        Text(text,x,y,size,color);
    }

    private void DrawSidebar(int x,int height)
    {
        var r=sim.Robots[selected];var team=sim.Team(r.Side);const int inset=20;var w=SidebarWidth-inset*2;
        Text($"操縦機体 / {r.Side.ToString().ToUpperInvariant()} {r.Id+1:00}",x+inset,97,17,Muted);
        FittedText(r.Spec.Name,x+inset,122,w,22);
        Text($"HP {r.Hp}/{sim.Rules.InitialHp}   弾 {(scenario.InfiniteAmmo?"∞":r.Ammo.ToString())} / {r.Spec.MagazineCapacity}",x+inset,155,17);
        Progress(x+inset,179,w,(float)r.Hp/sim.Rules.InitialHp,Accent);
        var control=sim.GetControlAvailability(r.Id);
        Text(control.Ready?"稼働中":AvailabilityText(control),x+inset,191,15,control.Ready?Accent:Warning);
        var contact=r.MovementBlockedBy;
        var motion=contact.HasFlag(MovementBlock.Robot)?"接触で移動制限 / 他の機体":contact.HasFlag(MovementBlock.Obstacle)?"接触で移動制限 / 壁・棚":contact.HasFlag(MovementBlock.RestrictedPit)?"進入制限 / 補給エリア":contact.HasFlag(MovementBlock.FieldBoundary)?"移動制限 / フィールド端":"接触による移動制限なし";
        Text(motion,x+inset,213,13,contact==MovementBlock.None?Muted:Warning);
        var fire=sim.GetFireAvailability(r.Id,AimYaw,AimPitch);
        Text($"射撃 / {KeyName("Fire")}",x+inset,237,14,Muted);
        FittedText(SessionRunning?fire.Ready?"射撃可能":AvailabilityText(fire):SessionLabel,x+inset,258,w,17,fire.Ready&&SessionRunning?Accent:Warning);
        var interval=sim.Seconds(sim.Ticks(r.Spec.ShotInterval));
        Progress(x+inset,283,w,1-(float)(sim.Seconds(r.NextShotAt-sim.Tick)/interval),fire.Ready?Accent:Warning);
        Text($"長押しで連射 / 間隔 {interval:0.000}秒 / {1/interval:0.0} 発/秒",x+inset,294,13,Muted);
        var outcome=r.LastShot.Outcome switch
        { ShotOutcome.Fired=>"最終射撃: 発射 / 飛行中",ShotOutcome.PanelHit=>$"最終射撃: 機体 {r.LastShot.TargetId+1:00} パネル命中",
            ShotOutcome.PanelNoDamage=>r.LastShot.Reason==ActionBlockReason.PanelHitCooldown?"パネル受付待機で無効 / 最大3Hz":AvailabilityText(new(r.LastShot.Reason)),ShotOutcome.BodyHit=>"最終射撃: 車体に接触 / ダメージなし",
            ShotOutcome.ObstacleHit=>"最終射撃: 壁・棚に接触",ShotOutcome.GroundHit=>"最終射撃: 着地",ShotOutcome.FieldExit=>"最終射撃: 場外",ShotOutcome.Expired=>"最終射撃: 飛行終了",_=>"接触状態でも射線が通れば射撃可能" };
        FittedText(AimLimited?"カメラは射出ジンバルの範囲外です":outcome,x+inset,313,w,13,AimLimited?Warning:Muted);
        var note=sim.Tick<r.InvulnerableUntil?$"復活無敵 残り {sim.Seconds(r.InvulnerableUntil-sim.Tick):0.0} 秒":r.PitAuthorized?"ピット入場許可済 / 自陣補給ゾーンへ":r.PitVisit&&r.PitUntil==0?"補給完了 / 補給エリアから退出してください":$"アーム {r.ArmHeight:0.000} m / {(r.ContainerId.HasValue?"コンテナ有":"コンテナ無")}";
        FittedText(note,x+inset,338,w,14,Muted);
        var interact=sim.GetInteractAvailability(r.Id,selectedCell);
        FittedText($"{KeyName("Interact")}: {(interact.Ready?r.ContainerId.HasValue?"選択スポットに設置可能":"回収可能":AvailabilityText(interact))}",x+inset,360,w,13,interact.Ready?Accent:Muted);
        string[] tabs=["スキル","運搬","ログ"];
        for(var i=0;i<tabs.Length;i++)if(Button(tabs[i],x+inset+i*106,386,100,28,sidebarTab==i)){sidebarTab=i;sidebarScroll=0;}
        var area=new Rectangle(x+10,423,SidebarWidth-20,Math.Max(1,height-84-423));
        var contentHeight=sidebarTab==0?470:sidebarTab==1?300:480;
        if(!menu && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(),area))sidebarScroll-=Raylib.GetMouseWheelMove()*34;
        sidebarScroll=Math.Clamp(sidebarScroll,0,Math.Max(0,contentHeight-area.Height));
        hitClip=area;Raylib.BeginScissorMode((int)area.X,(int)area.Y,(int)area.Width,(int)area.Height);
        var y=area.Y-sidebarScroll;
        if(sidebarTab==0)DrawSkills(x+inset,y,w,area);
        if(sidebarTab==1)DrawCargo(x+inset,y,w);
        if(sidebarTab==2)DrawEventLog(x+inset,y,w);
        Raylib.EndScissorMode();hitClip=null;
        if(contentHeight>area.Height)
        {
            var track=area.Height;var thumb=track*area.Height/contentHeight;
            Fill(x+SidebarWidth-7,area.Y+sidebarScroll/contentHeight*track,3,thumb,Muted);
        }
    }

    private void DrawSkills(float x,float y,float width,Rectangle area)
    {
        var robot=sim.Robots[selected];var state=sim.GetAllianceSkillState(robot.Side);
        Text(state.Phase==SkillPhase.EffectActive?$"{SkillName(state.Effect)} / 効果 {sim.Seconds(state.EffectTicks):0.0} 秒":state.Phase==SkillPhase.Cooldown?$"共通待機 / 残り {sim.Seconds(state.CooldownTicks):0.0} 秒":"共通スキル: 使用可能",x,y,16,state.Phase==SkillPhase.Ready?Accent:Warning);
        Text(state.Phase==SkillPhase.EffectActive?$"効果終了後に待機 {sim.Seconds(state.CooldownTicks):0.0} 秒":$"効果終了後 {sim.Rules.SkillCooldownSeconds:0.0} 秒待機 / ピット・サプライは例外",x,y+23,12,Muted);
        for(var i=0;i<Skills.Length;i++)
        {
            var definition=SkillCatalog.Find(Skills[i].Skill)!;var status=sim.GetSkillAvailability(robot.Side,definition.Skill,robot.Id);
            var by=y+53+i*44;
            var hover=Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(),area);
            var tooltip=$"{Skills[i].Name} / {definition.Cost} RP\n{SkillDescription(definition.Skill)}\n"+
                (definition.DurationSeconds>0?$"効果 {definition.DurationSeconds:0} 秒 → 待機 {sim.Rules.SkillCooldownSeconds:0.0} 秒\n":"")+
                (SessionRunning?AvailabilityText(status):SessionLabel);
            if(Button("",x,by,width,39,state.Effect==definition.Skill,status.Ready&&SessionRunning,hover?tooltip:null))pendingSkill=definition.Skill;
            var colour=status.Ready?White:Muted;
            Text($"{KeyName(definition.Skill.ToString())}  {Skills[i].Name}",x+10,by+4,15,colour);
            Text($"{definition.Cost} RP",x+width-67,by+4,14,colour);
            FittedText(status.Ready?(definition.ExemptFromCooldown?"共通待機の対象外":definition.DurationSeconds>0?$"効果 {definition.DurationSeconds:0}秒 + 待機 {sim.Rules.SkillCooldownSeconds:0.0}秒":$"即時効果 + 待機 {sim.Rules.SkillCooldownSeconds:0.0}秒"):AvailabilityText(status),x+10,by+23,width-20,12,status.Ready?Accent:Warning);
        }
    }

    private void DrawCargo(float x,float y,float width)
    {
        var robot=sim.Robots[selected];var team=sim.Team(robot.Side);
        Text("設置先を選択",x,y,17);
        for(var cell=0;cell<9;cell++)
        {
            var filled=(team.SpotMask&(1<<cell))!=0;var py=y+31+(2-cell/3)*39;
            if(Button($"{cell+1}{(filled?" / 済":"")}",x+cell%3*105,py,99,33,selectedCell==cell))selectedCell=cell;
        }
        var height=Arena.SpotPosition(robot.Side,selectedCell).Y-.06f;
        Text($"スポット {selectedCell+1} / 目標アーム高さ {height:0.000} m",x,y+161,15,Accent);
        Text($"{KeyName("ArmUp")} / {KeyName("ArmDown")} で高さ調整",x,y+187,14,Muted);
        var status=sim.GetInteractAvailability(robot.Id,selectedCell);
        if(Button($"{KeyName("Interact")}  {(robot.ContainerId.HasValue?"設置":"回収")}",x,y+217,width,35,enabled:status.Ready&&SessionRunning,tooltip:AvailabilityText(status)))pendingInteract=true;
        Text($"予備 {team.Reserve}枚 / 容量 {robot.Spec.MagazineCapacity}枚",x,y+268,14,Muted);
    }

    private string EventText(SimEvent e) => e.Kind switch
    {
        "hit"=>$"機体 {e.RobotId+1:00} / -{e.Value} HP",
        "ko"=>$"機体 {e.RobotId+1:00} 撃破 / {sim.Rules.RespawnSeconds:0}秒で復活",
        "revive"=>$"機体 {e.RobotId+1:00} 復活 / 無敵 {sim.Rules.InvulnerabilitySeconds:0}秒",
        "skill"=>$"{SkillName(e.Skill)} 発動 / -{e.Value} RP",
        "skill-blocked"=>$"{SkillName(e.Skill)}: {AvailabilityText(new(e.Reason,e.RemainingTicks,e.MissingRp))}",
        "interact-blocked"=>AvailabilityText(new(e.Reason)),
        "pit"=>$"機体 {e.RobotId+1:00} 補給開始 / {sim.Rules.PitSeconds:0}秒",
        "pit-complete"=>$"機体 {e.RobotId+1:00} 補給完了 / +{e.Value}枚",
        "stop"=>$"機体 {e.RobotId+1:00} 非常停止",
        "pickup"=>$"機体 {e.RobotId+1:00} コンテナ回収",
        "impact"=>(ShotOutcome)e.Value switch
        { ShotOutcome.PanelHit=>$"射撃 → 機体 {e.RelatedRobotId+1:00} パネル命中",ShotOutcome.PanelNoDamage=>AvailabilityText(new(e.Reason,e.RemainingTicks)),ShotOutcome.BodyHit=>"射撃 → 車体に接触 / ダメージなし",ShotOutcome.ObstacleHit=>"射撃 → 壁・棚に接触",ShotOutcome.GroundHit=>"射撃 → 着地",_=>"射撃 → 飛行終了" },
        _=>e.Message
    };

    private void DrawEventLog(float x,float y,float width)
    {
        Text("最近の操作・命中結果",x,y,17);
        var entries=sim.Events.Where(e=>e.Kind!="shot" && (e.RobotId<0 || e.RobotId==selected || e.RelatedRobotId==selected)).TakeLast(7).Reverse().ToArray();
        for(var i=0;i<entries.Length;i++)
        {
            Text($"{sim.Seconds(entries[i].Tick):0.0}s",x,y+33+i*46,12,Muted);
            FittedText(EventText(entries[i]),x,y+49+i*46,width,14);
        }
        if(settings.ShowMinimap)
        {
            var my=y+366;Fill(x,my,width,105,new(28,43,60,255));
            foreach(var o in sim.Obstacles)Raylib.DrawCircle((int)(x+o.Center.X/sim.Rules.FieldWidth*width),(int)(my+o.Center.Z/sim.Rules.FieldDepth*105),4,Muted);
            foreach(var r in sim.Robots)
            {
                var p=new Vector2(x+r.Position.X/sim.Rules.FieldWidth*width,my+r.Position.Z/sim.Rules.FieldDepth*105);
                Raylib.DrawCircleV(p,r.Id==selected?5:3,r.Side==Side.Red?Danger:new(103,169,255,255));
            }
        }
    }

    private void DrawAim(Camera3D camera,int width,int height,int top)
    {
        var robot=sim.Robots[selected];var ready=sim.GetFireAvailability(selected,AimYaw,AimPitch).Ready && SessionRunning;
        int cx=width/2,cy=top+height/2;var colour=ready?Accent:Warning;
        Raylib.DrawLine(cx-12,cy,cx+12,cy,Muted);Raylib.DrawLine(cx,cy-12,cx,cy+12,Muted);
        var yaw=robot.Yaw+AimYaw;
        var direction=new Vector3(MathF.Cos(yaw)*MathF.Cos(AimPitch),MathF.Sin(AimPitch),MathF.Sin(yaw)*MathF.Cos(AimPitch));
        var target=robot.Position+new Vector3(0,robot.Spec.ShotHeight,0)+direction*5;
        if(Vector3.Dot(target-camera.Position,camera.Target-camera.Position)>0)
        {
            var point=Raylib.GetWorldToScreenEx(target,camera,width,height)+new Vector2(0,top);
            if(point.X is > 12 && point.X<width-12 && point.Y>top+12 && point.Y<top+height-12)
            { Raylib.DrawCircleLines((int)point.X,(int)point.Y,7,colour);Text("射出",point.X+13,point.Y-7,12,colour); }
        }
        Text(AimLimited?"射出方向がカメラと異なります / 機体を旋回してください":$"{KeyName("Fire")} / {(ready?"射撃可能":"射撃状態は右パネルで確認")}",22,top+height-30,14,colour);
    }

    private void DrawTooltip(int width,int height)
    {
        if(hoverTip is null)return;
        const int boxWidth=370;var lines=new List<string>();
        foreach(var paragraph in hoverTip.Split('\n'))
        {
            var line="";
            foreach(var c in paragraph)
            {
                if(Raylib.MeasureTextEx(font,line+c,14,1).X>boxWidth-28){lines.Add(line);line="";}
                line+=c;
            }
            lines.Add(line);
        }
        var mouse=Raylib.GetMousePosition();var boxHeight=lines.Count*21+24;
        var x=Math.Clamp(mouse.X-boxWidth-12,10,width-boxWidth-10);var y=Math.Clamp(mouse.Y+20,10,Math.Max(10,height-boxHeight-10));
        Fill(x,y,boxWidth,boxHeight,new(8,15,25,248));Raylib.DrawRectangleLines((int)x,(int)y,boxWidth,boxHeight,Muted);
        for(var i=0;i<lines.Count;i++)Text(lines[i],x+14,y+12+i*21,14);
    }
}
