using System.Numerics;

namespace TSimulator.Core;

public enum ActionBlockReason
{
    Ready, RoundFinished, InvalidTarget, InvalidCommand, EmergencyStopped, KnockedOut, PitRefilling,
    OutOfAmmo, ShotCooldown, ProjectileCapacity, MuzzleObstructed, EffectActive, SkillCooldown,
    InsufficientRp, PitAlreadyAuthorized, PitVisitInProgress, NoKnockedOutRobot,
    SpotOccupied, ArmHeightMismatch, OutOfReach, NoContainerNearby, PanelHitCooldown, RespawnInvulnerability, BarrierInvulnerability
}

public readonly record struct ActionAvailability(ActionBlockReason Reason, long RemainingTicks = 0, int MissingRp = 0)
{
    public bool Ready => Reason == ActionBlockReason.Ready;
}

public enum RobotPhase { Active, KnockedOut, EmergencyStopped, PitRefilling }
[Flags] public enum MovementBlock { None = 0, FieldBoundary = 1, Obstacle = 2, Robot = 4, RestrictedPit = 8 }
public enum SkillPhase { Ready, EffectActive, Cooldown }
public enum ShotOutcome { None, Fired, PanelHit, PanelNoDamage, BodyHit, ObstacleHit, GroundHit, FieldExit, Expired }
public readonly record struct ShotFeedback(ShotOutcome Outcome, long Tick, int TargetId = -1,
    ActionBlockReason Reason = ActionBlockReason.Ready, long RemainingTicks = 0);
public readonly record struct AllianceSkillState(SkillPhase Phase, Skill Effect, long EffectTicks, long CooldownTicks)
{
    public long ReadyInTicks => EffectTicks + CooldownTicks;
}

public sealed record SkillDefinition(Skill Skill, int Cost, float DurationSeconds, bool ExemptFromCooldown);
public static class SkillCatalog
{
    public static IReadOnlyList<SkillDefinition> All { get; } = Array.AsReadOnly(new SkillDefinition[]
    {
        new(Skill.PitIn,20,0,true), new(Skill.Supply,100,0,true),
        new(Skill.Healing1,50,0,false), new(Skill.Healing2,100,0,false),
        new(Skill.Boost1,50,20,false), new(Skill.Boost2,90,40,false),
        new(Skill.Barrier1,80,20,false), new(Skill.Barrier2,150,15,false),
        new(Skill.Regenerate,100,0,false)
    });
    public static SkillDefinition? Find(Skill skill) => (int)skill is >= 1 and <= 9 ? All[(int)skill-1] : null;
}

// These queries are side-effect free. Command execution and the HUD use the same decisions.
public sealed partial class Simulation
{
    private readonly Dictionary<(Side Side,int Target,string Kind,Skill Skill,ActionBlockReason Reason),long> rejectionLogTicks=[];
    private void ReportRejection(Side side,int robotId,string kind,Skill skill,ActionAvailability availability)
    {
        var target=robotId>=0 && robotId<robots.Count?robotId:-1;
        var key=(side,target,kind,skill,availability.Reason);
        if(rejectionLogTicks.TryGetValue(key,out var previous) && Tick-previous<Ticks(1))return;
        rejectionLogTicks[key]=Tick;
        Log(kind,$"{side}: {skill} / {availability.Reason}",target,skill:skill,reason:availability.Reason,
            remainingTicks:availability.RemainingTicks,missingRp:availability.MissingRp);
    }
    public double Seconds(long ticks) => Math.Max(0, ticks) / (double)Rules.TickRate;
    public RobotPhase GetRobotPhase(int robotId)
    {
        var robot = robots[robotId];
        return robot.Stopped ? RobotPhase.EmergencyStopped : !robot.Alive ? RobotPhase.KnockedOut
            : robot.PitUntil > Tick ? RobotPhase.PitRefilling : RobotPhase.Active;
    }

    public ActionAvailability GetControlAvailability(int robotId)
    {
        if (robotId < 0 || robotId >= robots.Count) return new(ActionBlockReason.InvalidTarget);
        if (Finished) return new(ActionBlockReason.RoundFinished);
        var robot = robots[robotId];
        return GetRobotPhase(robotId) switch
        {
            RobotPhase.EmergencyStopped => new(ActionBlockReason.EmergencyStopped),
            RobotPhase.KnockedOut => new(ActionBlockReason.KnockedOut, Math.Max(0, robot.RespawnAt - Tick)),
            RobotPhase.PitRefilling => new(ActionBlockReason.PitRefilling, robot.PitUntil - Tick),
            _ => new(ActionBlockReason.Ready)
        };
    }

    public ActionAvailability GetFireAvailability(int robotId, float aimYaw = 0, float aimPitch = 0)
    {
        var control = GetControlAvailability(robotId);
        if (!control.Ready) return control;
        if (!float.IsFinite(aimYaw) || !float.IsFinite(aimPitch)) return new(ActionBlockReason.InvalidCommand);
        var robot = robots[robotId];
        if (!Scenario.InfiniteAmmo && robot.Ammo <= 0) return new(ActionBlockReason.OutOfAmmo);
        if (Tick < robot.NextShotAt) return new(ActionBlockReason.ShotCooldown, robot.NextShotAt - Tick);
        if (projectiles.Count >= Scenario.Physics.MaxProjectiles) return new(ActionBlockReason.ProjectileCapacity);
        if (!legacyReplay)
        {
            var (origin, muzzle, _) = ShotGeometry(robot, aimYaw, aimPitch);
            foreach (var obstacle in Obstacles)
                if (obstacle.Sweep(origin, muzzle, DiscExpansion, out _)) return new(ActionBlockReason.MuzzleObstructed);
        }
        return new(ActionBlockReason.Ready);
    }

    public ActionAvailability GetDamageAvailability(int robotId, int panel = -1)
    {
        if(robotId<0 || robotId>=robots.Count || panel is < -1 or > 3)return new(ActionBlockReason.InvalidTarget);
        if(Finished)return new(ActionBlockReason.RoundFinished);
        var target=robots[robotId];
        if(target.Stopped)return new(ActionBlockReason.EmergencyStopped);
        if(!target.Alive)return new(ActionBlockReason.KnockedOut,Math.Max(0,target.RespawnAt-Tick));
        if(Tick<target.InvulnerableUntil)return new(ActionBlockReason.RespawnInvulnerability,target.InvulnerableUntil-Tick);
        var team=Team(target.Side);
        if(team.Effect==Skill.Barrier2 && Tick<team.EffectUntil)return new(ActionBlockReason.BarrierInvulnerability,team.EffectUntil-Tick);
        if(panel>=0 && Tick<target.NextPanelHit[panel])return new(ActionBlockReason.PanelHitCooldown,target.NextPanelHit[panel]-Tick);
        return new(ActionBlockReason.Ready);
    }

    public AllianceSkillState GetAllianceSkillState(Side side)
    {
        var team = Team(side);
        var effectTicks = Math.Max(0, team.EffectUntil - Tick);
        var cooldownTicks = Math.Max(0, team.SkillReadyAt - Math.Max(Tick, team.EffectUntil));
        return new(effectTicks > 0 ? SkillPhase.EffectActive : cooldownTicks > 0 ? SkillPhase.Cooldown : SkillPhase.Ready,
            effectTicks > 0 ? team.Effect : Skill.None, effectTicks, cooldownTicks);
    }

    public ActionAvailability GetSkillAvailability(Side side, Skill skill, int targetId = -1)
    {
        if (Finished) return new(ActionBlockReason.RoundFinished);
        var definition = SkillCatalog.Find(skill);
        if (definition is null) return new(ActionBlockReason.InvalidCommand);
        var team = Team(side);
        if (!definition.ExemptFromCooldown)
        {
            var state = GetAllianceSkillState(side);
            if (state.Phase != SkillPhase.Ready)
                return new(state.Phase == SkillPhase.EffectActive ? ActionBlockReason.EffectActive : ActionBlockReason.SkillCooldown, state.ReadyInTicks);
        }
        if (team.Rp < definition.Cost) return new(ActionBlockReason.InsufficientRp, MissingRp: definition.Cost - team.Rp);
        if (skill == Skill.PitIn)
        {
            if (targetId < 0 || targetId >= robots.Count || robots[targetId].Side != side) return new(ActionBlockReason.InvalidTarget);
            var target = robots[targetId];
            if (target.Stopped) return new(ActionBlockReason.EmergencyStopped);
            if (!target.Alive) return new(ActionBlockReason.KnockedOut, Math.Max(0,target.RespawnAt - Tick));
            if (target.PitAuthorized) return new(ActionBlockReason.PitAlreadyAuthorized);
            if (target.PitVisit) return new(ActionBlockReason.PitVisitInProgress, Math.Max(0,target.PitUntil - Tick));
        }
        if (skill == Skill.Regenerate && RegenerateTarget(side, targetId) is null) return new(ActionBlockReason.NoKnockedOutRobot);
        return new(ActionBlockReason.Ready);
    }

    private Robot? RegenerateTarget(Side side, int targetId)
    {
        var target = targetId >= 0 && targetId < robots.Count ? robots[targetId] : null;
        return target is not null && target.Side == side && !target.Alive && !target.Stopped ? target
            : robots.FirstOrDefault(r => r.Side == side && !r.Alive && !r.Stopped);
    }

    public ActionAvailability GetInteractAvailability(int robotId, int cell = -1)
    {
        var control = GetControlAvailability(robotId);
        if (!control.Ready) return control;
        var robot = robots[robotId];
        if (!robot.ContainerId.HasValue)
            return new(FindPickup(robot) is null ? ActionBlockReason.NoContainerNearby : ActionBlockReason.Ready);
        if (cell is < -1 or > 8) return new(ActionBlockReason.InvalidTarget);
        if (cell >= 0) return GetSpotAvailability(robot, cell);
        var closest = Enumerable.Range(0,9).MinBy(c => Vector3.DistanceSquared(robot.Gripper,Arena.SpotPosition(robot.Side,c)));
        for (var c = 0; c < 9; c++) if (GetSpotAvailability(robot,c).Ready) return new(ActionBlockReason.Ready);
        return GetSpotAvailability(robot,closest);
    }

    private ActionAvailability GetSpotAvailability(Robot robot, int cell)
    {
        if ((Team(robot.Side).SpotMask & (1 << cell)) != 0) return new(ActionBlockReason.SpotOccupied);
        var goal = Arena.SpotPosition(robot.Side,cell);
        if (Math.Abs(robot.Gripper.Y-goal.Y) > .13f) return new(ActionBlockReason.ArmHeightMismatch);
        return new(Vector3.Distance(robot.Gripper,goal) > .28f ? ActionBlockReason.OutOfReach : ActionBlockReason.Ready);
    }

    private Container? FindPickup(Robot robot)
    {
        Container? nearest = null; var distance = .24f * .24f;
        foreach (var container in containers)
        {
            if (container.Deposited || container.HeldBy.HasValue || (container.Side.HasValue && container.Side != robot.Side)) continue;
            var d = Vector3.DistanceSquared(container.Position,robot.Gripper);
            if (d < distance && container.Position.Y-.06f >= robot.Spec.ArmMinHeight-.015f) { nearest=container; distance=d; }
        }
        return nearest;
    }

    private Vector3 DiscExpansion => new(Rules.DiscRadius,Rules.DiscThickness / 2,Rules.DiscRadius);
    private (Vector3 Origin, Vector3 Muzzle, Vector3 Direction) ShotGeometry(Robot robot, float aimYaw, float aimPitch)
    {
        var yaw = robot.Yaw + Math.Clamp(aimYaw,-.8f,.8f); var pitch = Math.Clamp(aimPitch,-.3f,.5f);
        var direction = new Vector3(MathF.Cos(yaw)*MathF.Cos(pitch),MathF.Sin(pitch),MathF.Sin(yaw)*MathF.Cos(pitch));
        var origin = robot.Position + new Vector3(0,robot.Spec.ShotHeight,0);
        return (origin,origin + direction * (robot.Spec.Depth/2 + Rules.DiscRadius + .01f),direction);
    }

    private readonly record struct Contact(float Fraction, ShotOutcome Outcome, Robot? Robot = null, int Panel = -1);
    private Contact FirstContact(Vector3 start, Vector3 end, Vector3 velocity, int ownerId, bool ignoreOwner)
    {
        var first = new Contact(2,ShotOutcome.None);
        foreach (var obstacle in Obstacles)
            if (obstacle.Sweep(start,end,DiscExpansion,out var t) && t < first.Fraction) first=new(t,ShotOutcome.ObstacleHit);
        foreach (var robot in robots)
        {
            if (ignoreOwner && robot.Id == ownerId) continue;
            var bodyHit = robot.Body.Sweep(start,end,DiscExpansion,out var bodyT);
            for (var panel = 0; panel < 4; panel++)
            {
                var box = Panel(robot,panel);
                if (Vector3.Dot(velocity,new Vector3(MathF.Cos(box.Yaw),0,MathF.Sin(box.Yaw))) >= 0) continue;
                if (box.Sweep(start,end,DiscExpansion,out var t) && t < first.Fraction && (!bodyHit || t <= bodyT + .002f))
                    first=new(t,ShotOutcome.PanelHit,robot,panel);
            }
            if (bodyHit && bodyT < first.Fraction - .002f) first=new(bodyT,ShotOutcome.BodyHit,robot);
        }
        return first;
    }

    private void ResolveContact(int ownerId, long firedAt, Contact contact)
    {
        var outcome = contact.Outcome;
        var availability=new ActionAvailability(ActionBlockReason.Ready);
        if (contact.Robot is { } target && contact.Panel >= 0)
        {
            availability=GetDamageAvailability(target.Id,contact.Panel);
            var hp = target.Hp;
            if (Tick >= target.NextPanelHit[contact.Panel])
            {
                ApplyHit(robots[ownerId].Side,target.Id);
                target.NextPanelHit[contact.Panel]=Tick + Ticks(Rules.PanelHitInterval);
            }
            if (target.Hp == hp) outcome=ShotOutcome.PanelNoDamage;
        }
        RecordShotOutcome(ownerId,firedAt,outcome,contact.Robot?.Id ?? -1,availability);
    }

    private void RecordShotOutcome(int ownerId, long firedAt, ShotOutcome outcome, int targetId = -1, ActionAvailability availability = default)
    {
        var owner = robots[ownerId];
        // An older disc landing must not overwrite feedback for a more recent shot.
        if (owner.LastShotAt == firedAt) owner.LastShot=new(outcome,Tick,targetId,availability.Reason,availability.RemainingTicks);
        Log("impact", $"{ownerId + 1}: {outcome}",ownerId,targetId,(int)outcome,reason:availability.Reason,remainingTicks:availability.RemainingTicks);
    }
}
