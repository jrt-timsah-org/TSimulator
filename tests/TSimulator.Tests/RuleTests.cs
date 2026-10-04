using System.Numerics;
using TSimulator.Core;
namespace TSimulator.Tests;

public class RuleTests
{
    private static void Advance(Simulation sim,int count) { for(var i=0;i<count;i++) sim.Step([]); }
    [Fact] public void OfficialDefaultsAreCoRE2V272()
    {
        var sim=new Simulation(new());
        Assert.Equal("V27.2.0",sim.Rules.Version);Assert.Equal(4,sim.Robots.Count);
        Assert.All(sim.Robots,r=>Assert.Equal(60,r.Hp));Assert.Equal(200,sim.Team(Side.Red).Reserve+sim.Robots.Where(r=>r.Side==Side.Red).Sum(r=>r.Ammo));
        Assert.Equal(18,sim.Containers.Count);
    }
    [Fact] public void FriendlyFireIsDamageButNotEnemyDamageStatistic()
    {
        var sim=new Simulation(new());sim.ApplyHit(Side.Red,0);
        Assert.Equal(50,sim.Robots[0].Hp);Assert.Equal(0,sim.Team(Side.Red).Damage);
    }
    [Fact] public void KnockoutRespawnAndInvulnerabilityHaveExactBoundaries()
    {
        var sim=new Simulation(new());for(int i=0;i<6;i++)sim.ApplyHit(Side.Blue,0);
        Assert.False(sim.Robots[0].Alive);Assert.Equal(10,sim.Team(Side.Blue).Vp);
        Advance(sim,3600);Assert.False(sim.Robots[0].Alive);sim.Step([]);Assert.Equal(60,sim.Robots[0].Hp);
        sim.ApplyHit(Side.Blue,0);Assert.Equal(60,sim.Robots[0].Hp);
        Advance(sim,599);sim.ApplyHit(Side.Blue,0);Assert.Equal(50,sim.Robots[0].Hp);
    }
    [Fact] public void DoubleKnockoutIsAdditionalAndDoesNotRepeatEveryTick()
    {
        var sim=new Simulation(new());for(int r=0;r<2;r++)for(int i=0;i<6;i++)sim.ApplyHit(Side.Blue,r);
        sim.Step([]);Assert.Equal(60,sim.Team(Side.Blue).Vp);Advance(sim,20);Assert.Equal(60,sim.Team(Side.Blue).Vp);
    }
    [Fact] public void NegativeVpIsAllowed()
    { var sim=new Simulation(new());sim.Foul(Side.Red,"referee");Assert.Equal(-50,sim.Team(Side.Red).Vp); }
    [Fact] public void SpotAwardsAreUniqueAndLineAwardsCountOnce()
    {
        var sim=new Simulation(new());var ids=sim.Containers.Where(c=>c.Side==Side.Red).Select(c=>c.Id).ToArray();
        for(int c=0;c<3;c++)Assert.True(sim.Deposit(Side.Red,c,ids[c]));
        Assert.Equal(90,sim.Team(Side.Red).Vp);Assert.Equal(240,sim.Team(Side.Red).Rp);
        Assert.False(sim.Deposit(Side.Red,0,ids[3]));Assert.Equal(240,sim.Team(Side.Red).Rp);
    }
    [Fact] public void FullShelfHas390VpAndEightLines()
    {
        var sim=new Simulation(new());var ids=sim.Containers.Where(c=>c.Side!=Side.Blue).Select(c=>c.Id).Take(9).ToArray();
        for(int c=0;c<9;c++)Assert.True(sim.Deposit(Side.Red,c,ids[c]));
        Assert.Equal(390,sim.Team(Side.Red).Vp);Assert.Equal(890,sim.Team(Side.Red).Rp);Assert.Equal(255,sim.Team(Side.Red).LineMask);
    }
    [Fact] public void CannotUseOpponentContainer()
    {var sim=new Simulation(new());Assert.False(sim.Deposit(Side.Red,0,sim.Containers.First(c=>c.Side==Side.Blue).Id));}
    [Fact] public void TimeBonusArrivesOnceAt300Seconds()
    {var sim=new Simulation(new());Advance(sim,35999);Assert.Equal(100,sim.Team(Side.Red).Rp);sim.Step([]);Assert.Equal(150,sim.Team(Side.Red).Rp);Advance(sim,1000);Assert.Equal(150,sim.Team(Side.Red).Rp);}
    [Fact] public void InstantSkillHasFiveSecondCooldownWithExceptions()
    {
        var sim=new Simulation(new(){RpBonus=1000});Assert.True(sim.ActivateSkill(Side.Red,Skill.Healing1));
        Assert.False(sim.ActivateSkill(Side.Red,Skill.Boost1));Assert.True(sim.ActivateSkill(Side.Red,Skill.Supply));
        Assert.True(sim.ActivateSkill(Side.Red,Skill.PitIn,0));Advance(sim,599);Assert.False(sim.ActivateSkill(Side.Red,Skill.Boost1));
        sim.Step([]);Assert.True(sim.ActivateSkill(Side.Red,Skill.Boost1));
    }
    [Fact] public void TimedSkillBlocksOtherSkillsThroughEffectAndCooldown()
    {
        var sim=new Simulation(new(){RpBonus=1000});Assert.True(sim.ActivateSkill(Side.Red,Skill.Boost1));
        Advance(sim,2999);Assert.False(sim.ActivateSkill(Side.Red,Skill.Healing2));sim.Step([]);Assert.True(sim.ActivateSkill(Side.Red,Skill.Healing2));
    }
    [Fact] public void BoostAndBarrierMultiply()
    {var sim=new Simulation(new(){RpBonus=1000});sim.ActivateSkill(Side.Red,Skill.Boost1);sim.ActivateSkill(Side.Blue,Skill.Barrier1);sim.ApplyHit(Side.Red,2);Assert.Equal(50,sim.Robots[2].Hp);}
    [Fact] public void Barrier2MakesInvulnerable()
    {var sim=new Simulation(new(){RpBonus=1000});sim.ActivateSkill(Side.Blue,Skill.Barrier2);sim.ApplyHit(Side.Red,2);Assert.Equal(60,sim.Robots[2].Hp);}
    [Fact] public void HealingCannotReviveButRegenerateCan()
    {
        var sim=new Simulation(new(){RpBonus=1000});for(int i=0;i<6;i++)sim.ApplyHit(Side.Blue,0);
        sim.ActivateSkill(Side.Red,Skill.Healing2);Assert.False(sim.Robots[0].Alive);
        Advance(sim,600);Assert.True(sim.ActivateSkill(Side.Red,Skill.Regenerate,0));Assert.True(sim.Robots[0].Alive);
    }
    [Fact] public void EmergencyStopIsLatchedUntilNewRound()
    {
        var sim=new Simulation(new());sim.Step([new(0,EmergencyStop:true)]);var start=sim.Robots[0].Position;
        for(int i=0;i<120;i++)sim.Step([new(0,Forward:1,Fire:true)]);
        Assert.Equal(start,sim.Robots[0].Position);Assert.True(sim.Robots[0].Stopped);Assert.Empty(sim.Projectiles);
    }
    [Fact] public void AllTieBreakersEqualRequiresReferee()
    {var sim=new Simulation(new());Assert.Null(sim.Result().Winner);}
    [Fact] public void RoundsStopExactlyAndDoNotProcessFurtherCommands()
    {
        var sim=new Simulation(new(){Rules=new(){RoundSeconds=1}});Advance(sim,120);var hash=sim.StateHash();
        sim.Step([new(0,Forward:1)]);sim.ApplyHit(Side.Blue,0);sim.Foul(Side.Red,"late");Assert.Equal(hash,sim.StateHash());
    }
    [Theory] [InlineData(12.25f)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void InvalidShotSpeedRejected(float value) => Assert.Throws<InvalidDataException>(()=>new Simulation(new(){Robot=new(){ShotSpeed=value}}));
    [Fact] public void SandboxCanChangeCompetitionLimits()
    {var sim=new Simulation(new(){Sandbox=true,RobotsPerSide=1,Robot=new(){ShotSpeed=20}});Assert.Equal(2,sim.Robots.Count);}
    [Fact] public void DefaultKeyBindingsHaveNoConflicts() => new AppSettings().Validate();
    [Fact] public void SkillWithInvalidTargetDoesNotSpendRp()
    {var sim=new Simulation(new());Assert.False(sim.ActivateSkill(Side.Red,Skill.PitIn,2));Assert.Equal(100,sim.Team(Side.Red).Rp);}
}
