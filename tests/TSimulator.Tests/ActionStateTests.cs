using System.Numerics;
using TSimulator.Core;

namespace TSimulator.Tests;

public class ActionStateTests
{
    private static void Advance(Simulation sim,int ticks) { for(var i=0;i<ticks;i++)sim.Step([]); }
    private static Simulation ContactScene(float shotHeight=.1725f)
    {
        var sim=new Simulation(new() { BotsEnabled=false,Robot=new() { ShotHeight=shotHeight } });
        sim.Robots[0].Position=new(3,.011f,2);sim.Robots[1].Position=new(5,.011f,2);
        sim.Robots[2].Position=new(3.7f,.011f,2);sim.Robots[2].Yaw=MathF.PI;
        return sim;
    }
    [Fact] public void TouchingEnemyBlocksMovementButCanBeHitByShot()
    {
        var sim=ContactScene();var robot=sim.Robots[0];var position=robot.Position;
        Assert.True(sim.GetFireAvailability(0).Ready);
        sim.Step([new(0,Forward:1,Fire:true)]);
        Assert.Equal(position,robot.Position);Assert.Equal(MovementBlock.Robot,robot.MovementBlockedBy);
        Assert.Equal(29,robot.Ammo);Assert.Equal(50,sim.Robots[2].Hp);
        Assert.Equal(ShotOutcome.PanelHit,robot.LastShot.Outcome);
        Assert.Equal(2,robot.LastShot.TargetId);
    }
    [Fact] public void ShotAgainstTouchingChassisReportsNoPanelDamage()
    {
        var sim=ContactScene(.5f);sim.Step([new(0,Fire:true)]);
        Assert.Equal(60,sim.Robots[2].Hp);Assert.Equal(ShotOutcome.BodyHit,sim.Robots[0].LastShot.Outcome);
    }
    [Fact] public void RapidFireRespectsPanelRateAndReportsExactBlockReason()
    {
        var sim=ContactScene();
        // Replace the source profile through the scenario, retaining the same contact arrangement.
        var rapid=new Simulation(sim.Scenario with { Robot=new() { ShotInterval=.08f } });
        for(var i=0;i<sim.Robots.Count;i++){rapid.Robots[i].Position=sim.Robots[i].Position;rapid.Robots[i].Yaw=sim.Robots[i].Yaw;}
        for(var i=0;i<61;i++)rapid.Step([new(0,Fire:true)]);
        Assert.Equal(40,rapid.Robots[2].Hp);Assert.Equal(23,rapid.Robots[0].Ammo);
        Assert.Equal(ShotOutcome.PanelNoDamage,rapid.Robots[0].LastShot.Outcome);
        Assert.Equal(ActionBlockReason.PanelHitCooldown,rapid.Robots[0].LastShot.Reason);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void ProtectedPanelReportsRespawnOrBarrierRatherThanShotCooldown(bool respawn)
    {
        var sim=ContactScene();
        if(respawn)sim.Robots[2].InvulnerableUntil=600;
        else { sim.Team(Side.Blue).Rp=1000;sim.ActivateSkill(Side.Blue,Skill.Barrier2); }
        sim.Step([new(0,Fire:true)]);
        Assert.Equal(60,sim.Robots[2].Hp);Assert.Equal(29,sim.Robots[0].Ammo);
        Assert.Equal(respawn?ActionBlockReason.RespawnInvulnerability:ActionBlockReason.BarrierInvulnerability,sim.Robots[0].LastShot.Reason);
    }
    [Fact] public void WallContactDoesNotLockUnobstructedWeapon()
    {
        var sim=new Simulation(new() { BotsEnabled=false,ExtraObstacles=[new(3.45f,2,.2f,2,1)] });
        var robot=sim.Robots[0];robot.Position=new(3,.011f,2);robot.Yaw=MathF.PI;
        sim.Step([new(0,Forward:-1,Fire:true)]);
        Assert.Equal(MovementBlock.Obstacle,robot.MovementBlockedBy);Assert.Equal(29,robot.Ammo);
        Assert.Single(sim.Projectiles);Assert.Equal(ShotOutcome.Fired,robot.LastShot.Outcome);
    }
    [Fact] public void BlockedMuzzlePreservesAmmoAndDoesNotStartCooldown()
    {
        var sim=new Simulation(new() { ExtraObstacles=[new(1.2f,.6f,.2f,.6f,.9f)] });var robot=sim.Robots[0];
        Assert.Equal(ActionBlockReason.MuzzleObstructed,sim.GetFireAvailability(0).Reason);
        sim.Step([new(0,Fire:true)]);
        Assert.Equal(30,robot.Ammo);Assert.Equal(0,robot.NextShotAt);Assert.Empty(sim.Projectiles);
        robot.Yaw=MathF.PI;Assert.True(sim.GetFireAvailability(0).Ready);
        sim.Step([new(0,Fire:true)]);Assert.Equal(29,robot.Ammo);
    }
    [Fact] public void ShotCooldownBoundaryMatchesExecution()
    {
        var sim=new Simulation(new());sim.Step([new(0,Fire:true)]);var robot=sim.Robots[0];
        var status=sim.GetFireAvailability(0);Assert.Equal(ActionBlockReason.ShotCooldown,status.Reason);
        Assert.Equal(robot.NextShotAt-sim.Tick,status.RemainingTicks);
        Advance(sim,(int)status.RemainingTicks-1);Assert.False(sim.GetFireAvailability(0).Ready);
        sim.Step([new(0,Fire:true)]);Assert.Equal(29,robot.Ammo);Assert.True(sim.GetFireAvailability(0).Ready);
        sim.Step([new(0,Fire:true)]);Assert.Equal(28,robot.Ammo);
    }
    [Fact] public void EmptyMagazineAndInfiniteAmmoHaveDifferentAvailability()
    {
        var sim=new Simulation(new());sim.Robots[0].Ammo=0;Assert.Equal(ActionBlockReason.OutOfAmmo,sim.GetFireAvailability(0).Reason);
        var infinite=new Simulation(new() { Sandbox=true,InfiniteAmmo=true });infinite.Robots[0].Ammo=0;
        Assert.True(infinite.GetFireAvailability(0).Ready);infinite.Step([new(0,Fire:true)]);Assert.Equal(0,infinite.Robots[0].Ammo);
    }
    [Fact] public void EffectAndPostEffectCooldownAreSeparatePhases()
    {
        var sim=new Simulation(new() { RpBonus=1000 });sim.ActivateSkill(Side.Red,Skill.Boost1);
        var state=sim.GetAllianceSkillState(Side.Red);
        Assert.Equal(SkillPhase.EffectActive,state.Phase);Assert.Equal(2400,state.EffectTicks);Assert.Equal(600,state.CooldownTicks);
        Assert.Equal(ActionBlockReason.EffectActive,sim.GetSkillAvailability(Side.Red,Skill.Healing1).Reason);
        Assert.True(sim.GetSkillAvailability(Side.Red,Skill.Supply).Ready);
        Advance(sim,2400);state=sim.GetAllianceSkillState(Side.Red);
        Assert.Equal(SkillPhase.Cooldown,state.Phase);Assert.Equal(Skill.None,state.Effect);Assert.Equal(0,state.EffectTicks);
        Assert.Equal(600,state.CooldownTicks);Advance(sim,600);Assert.True(sim.GetSkillAvailability(Side.Red,Skill.Healing1).Ready);
    }
    [Fact] public void InvalidSkillTargetsAndRpShortageMatchExecution()
    {
        var sim=new Simulation(new());
        Assert.Equal(ActionBlockReason.NoKnockedOutRobot,sim.GetSkillAvailability(Side.Red,Skill.Regenerate,0).Reason);
        Assert.False(sim.ActivateSkill(Side.Red,Skill.Regenerate,0));Assert.Equal(100,sim.Team(Side.Red).Rp);
        var status=sim.GetSkillAvailability(Side.Red,Skill.Barrier2);
        Assert.Equal(ActionBlockReason.InsufficientRp,status.Reason);Assert.Equal(50,status.MissingRp);
        Assert.False(sim.ActivateSkill(Side.Red,Skill.Barrier2));
        sim.ActivateSkill(Side.Red,Skill.PitIn,0);
        Assert.Equal(ActionBlockReason.PitAlreadyAuthorized,sim.GetSkillAvailability(Side.Red,Skill.PitIn,0).Reason);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void StopOrKnockoutCancelsPendingPitRefill(bool stop)
    {
        var sim=new Simulation(new());var r=sim.Robots[0];r.Position=new(.75f,.011f,7.2f);r.Ammo=0;
        sim.ActivateSkill(Side.Red,Skill.PitIn,0);sim.Step([]);
        Assert.Equal(RobotPhase.PitRefilling,sim.GetRobotPhase(0));
        Assert.Equal(ActionBlockReason.PitRefilling,sim.GetFireAvailability(0).Reason);
        if(stop)sim.Step([new(0,EmergencyStop:true)]);else for(var i=0;i<6;i++)sim.ApplyHit(Side.Blue,0);
        Assert.Equal(0,r.PitUntil);Assert.False(r.PitAuthorized);
        Advance(sim,2401);Assert.Equal(0,r.Ammo);Assert.Equal(140,sim.Team(Side.Red).Reserve);
        Assert.Equal(stop?RobotPhase.EmergencyStopped:RobotPhase.KnockedOut,sim.GetRobotPhase(0));
    }
    [Fact] public void AvailabilityQueriesDoNotMutateState()
    {
        var sim=new Simulation(new());var hash=sim.StateHash();
        for(var i=0;i<20;i++)
        {
            sim.GetControlAvailability(0);sim.GetFireAvailability(0,.8f,.5f);sim.GetInteractAvailability(0,1);
            foreach(var skill in SkillCatalog.All)sim.GetSkillAvailability(Side.Red,skill.Skill,0);
            sim.GetAllianceSkillState(Side.Red);
        }
        Assert.Equal(hash,sim.StateHash());Assert.Single(sim.Events);
    }
    [Fact] public void FinishedRoundDisablesEveryAction()
    {
        var sim=new Simulation(new() { Rules=new() { RoundSeconds=1 } });Advance(sim,120);
        Assert.Equal(ActionBlockReason.RoundFinished,sim.GetControlAvailability(0).Reason);
        Assert.Equal(ActionBlockReason.RoundFinished,sim.GetFireAvailability(0).Reason);
        Assert.Equal(ActionBlockReason.RoundFinished,sim.GetInteractAvailability(0).Reason);
        Assert.All(SkillCatalog.All,s=>Assert.Equal(ActionBlockReason.RoundFinished,sim.GetSkillAvailability(Side.Red,s.Skill,0).Reason));
    }
    [Fact] public void VersionOneContactReplayStillVerifiesOriginalBehavior()
    {
        var sim=Replay.Play(Path.Combine(AppContext.BaseDirectory,"Fixtures","v1-muzzle-contact.jsonl"));
        Assert.Equal(3,sim.Tick);Assert.Equal(29,sim.Robots[0].Ammo);
    }
}
