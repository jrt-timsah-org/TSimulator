using System.Numerics;
using TSimulator.Core;
namespace TSimulator.Tests;
public class PhysicsTests
{
    [Fact] public void ShotHitsPanelWithoutChassisDamageShortcut()
    {
        var sim = new Simulation(new());
        sim.Robots[1].Position = new(3,.011f,2);
        sim.Robots[2].Position = new(2.3f,.011f,.6f);
        sim.Step([new(0,Fire:true)]);
        for (int i=0;i<40;i++) sim.Step([]);
        Assert.Equal(50,sim.Robots[2].Hp);
    }
    [Fact] public void PitRefillWaitsTwentySecondsAndUsesAllianceReserve()
    {
        var sim = new Simulation(new()); var robot=sim.Robots[0];
        robot.Position=new(.75f,.011f,7.2f);robot.Ammo=0;
        Assert.True(sim.ActivateSkill(Side.Red,Skill.PitIn,0));
        sim.Step([]);Assert.Equal(0,robot.Ammo);
        for (int i=0;i<2399;i++) sim.Step([]);
        Assert.Equal(0,robot.Ammo);sim.Step([]);
        Assert.Equal(30,robot.Ammo);Assert.Equal(110,sim.Team(Side.Red).Reserve);
        Assert.Equal(80,sim.Team(Side.Red).Rp);
    }
    [Fact] public void ContinuousCollisionFindsThinPanelBetweenSteps()
    {
        var box=new Box(new(3,.17f,0),new(.007f,.0725f,.0725f));
        Assert.True(box.Sweep(new(0,.17f,0),new(6,.17f,0),new(.09f,.01f,.09f),out var t));Assert.InRange(t,.48f,.51f);
    }
    [Fact] public void RotatedObstacleIsNotItsAxisAlignedBounds()
    {
        var box=new Box(new(6,0,4),new(1.2f,1,.2f),-MathF.PI/4);
        Assert.False(box.Intersects(new(new(6.8f,0,4.8f),new(.1f,1,.1f))));
        Assert.True(box.Intersects(new(new(6.6f,0,3.4f),new(.1f,1,.1f))));
    }
    [Fact] public void RobotCannotLeaveFieldAndDiagonalSpeedIsNormalized()
    {
        var sim=new Simulation(new(){BotsEnabled=false});
        for(int i=0;i<600;i++)sim.Step([new(0,Forward:-1,Strafe:-1)]);
        Assert.True(sim.Robots[0].Position.X>=.35f);Assert.True(sim.Robots[0].Position.Z>=.35f);
        Assert.True(sim.Robots[0].Velocity.Length()<=sim.Scenario.Robot.MaxSpeed+.0001f);
    }
    [Fact] public void NonFiniteCommandsDoNotPoisonSimulation()
    {var sim=new Simulation(new());sim.Step([new(0,Forward:float.NaN)]);Assert.True(float.IsFinite(sim.Robots[0].Position.X));}
    [Fact] public void ProjectilePoolIsBounded()
    {
        var sim=new Simulation(new(){Sandbox=true,InfiniteAmmo=true,Physics=new(){MaxProjectiles=1,Gravity=0,Lift=0,Drag=0}});
        for(int i=0;i<120;i++) {sim.Step([new(0,Fire:true)]);Assert.InRange(sim.Projectiles.Count,0,1);}
    }
    [Fact] public void RecordedCommandsReproduceFullState()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".jsonl");
        try
        {
            var sim=new Simulation(new());var bot=new PracticeBot();
            using(var writer=new ReplayWriter(path,sim.Scenario))
            {
                for(int i=0;i<2000;i++)
                {var commands=sim.Robots.Select(r=>bot.GetCommand(sim,r)).ToArray();writer.Write(commands);sim.Step(commands);}
                writer.Complete(sim);
            }
            var replay=Replay.Play(path);Assert.Equal(sim.StateHash(),replay.StateHash());
        }
        finally {File.Delete(path);}
    }
    [Fact] public void TruncatedReplayIsRejected()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".jsonl");
        try {using(var w=new ReplayWriter(path,new()))w.Write([]);Assert.Throws<InvalidDataException>(()=>Replay.Play(path));}
        finally{File.Delete(path);}
    }
    [Fact] public void ParserReadsOnlyJsonAndNeverExecutesPageScripts()
    {
        using var data=OfficialAssets.Extract("window.F3D = {\"scale\":4,\"groups\":[]}; alert('untrusted');","F3D");
        Assert.Equal(4,data.RootElement.GetProperty("scale").GetInt32());
    }
}
