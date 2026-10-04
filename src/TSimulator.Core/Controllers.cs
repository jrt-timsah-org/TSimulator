using System.Numerics;
namespace TSimulator.Core;

public interface IRobotController { RobotCommand GetCommand(Simulation simulation, Robot robot); }
public sealed class PracticeBot : IRobotController
{
    public RobotCommand GetCommand(Simulation sim, Robot robot)
    {
        if (!robot.Alive || robot.Stopped || robot.PitUntil > sim.Tick) return new(robot.Id);
        var other = sim.Robots.Where(r => r.Side != robot.Side && r.Alive && !r.Stopped)
            .MinBy(r => Vector3.DistanceSquared(robot.Position, r.Position));
        var team = sim.Team(robot.Side);
        var target = other?.Position ?? new Vector3(6, 0, 4);
        bool interact = false; float arm = 0; int cell = -1;
        var carrier = robot.Id % sim.Scenario.RobotsPerSide == 1;
        if (robot.Ammo == 0 && !sim.Scenario.InfiniteAmmo)
        {
            if (!robot.PitAuthorized && !robot.PitVisit) return new(robot.Id, Skill: Skill.PitIn);
            target = robot.Side == Side.Red ? new(.75f, .011f, 7.2f) : new(11.25f, .011f, .8f);
            // Route around the resupply divider's open end.
            if (robot.Side == Side.Red && robot.Position.X > 1.5f && robot.Position.Z > 5.05f) target = new(2.6f, .011f, 4.85f);
            if (robot.Side == Side.Blue && robot.Position.X < 10.5f && robot.Position.Z < 2.95f) target = new(9.4f, .011f, 3.15f);
        }
        else if (carrier && team.SpotMask != 511)
        {
            if (robot.ContainerId.HasValue)
            {
                cell = Enumerable.Range(0, 9).First(c => (team.SpotMask & (1 << c)) == 0);
                var spot = Arena.SpotPosition(robot.Side, cell);
                target = spot + new Vector3(robot.Side == Side.Red ? 1.03f : -1.03f, -spot.Y + .011f, 0);
                arm = Math.Clamp((spot.Y - .06f - robot.ArmHeight) * 10, -1, 1);
                interact = true;
            }
            else
            {
                var container = sim.Containers.Where(c => !c.Deposited && !c.HeldBy.HasValue
                    && (!c.Side.HasValue || c.Side == robot.Side) && c.Position.Y >= .3f)
                    .MinBy(c => Vector3.DistanceSquared(robot.Position, c.Position));
                if (container is not null)
                {
                    target = container.Position + new Vector3(robot.Side == Side.Red ? 1.03f : -1.03f, -container.Position.Y + .011f, 0);
                    arm = Math.Clamp((container.Position.Y - .06f - robot.ArmHeight) * 10, -1, 1); interact = true;
                }
            }
        }
        var delta = target - robot.Position; delta.Y = 0;
        var direction = delta.LengthSquared() > .02f ? Vector3.Normalize(delta) : Vector3.Zero;
        // Local obstacle avoidance, deterministic and intentionally replaceable via IRobotController.
        foreach (var box in sim.Obstacles)
        {
            var local = box.Local(robot.Position);
            var closest = box.World(new(Math.Clamp(local.X, -box.HalfSize.X, box.HalfSize.X), local.Y, Math.Clamp(local.Z, -box.HalfSize.Z, box.HalfSize.Z)));
            var away = robot.Position - closest; away.Y = 0; var d = away.Length();
            if (d > .001f && d < .85f) direction += away / d * (.85f - d) * 3;
        }
        foreach (var r in sim.Robots)
        {
            if (r.Id == robot.Id) continue;
            var away = robot.Position - r.Position; var d = away.Length();
            if (d > .01f && d < 1) direction += away / d * (1 - d) * 2;
        }
        if (direction.LengthSquared() > 1) direction = Vector3.Normalize(direction);
        var aimTarget = carrier && interact ? target + new Vector3(robot.Side == Side.Red ? -1 : 1, 0, 0) : other?.Position ?? target;
        var aim = aimTarget - robot.Position; var yaw = MathF.Atan2(aim.Z, aim.X);
        var error = Simulation.Wrap(yaw - robot.Yaw);
        var forward = Vector3.Dot(direction, robot.Forward);
        var strafe = Vector3.Dot(direction, new Vector3(-robot.Forward.Z, 0, robot.Forward.X));
        var clear = other is not null && !sim.Obstacles.Any(b => b.Sweep(robot.Position + new Vector3(0,.18f,0), other.Position + new Vector3(0,.18f,0), new(.09f,.01f,.09f), out _));
        return new(robot.Id, forward, strafe, Math.Clamp(error * 3, -1, 1),
            Fire: !carrier && clear && Math.Abs(error) < .08f && aim.Length() < 4.5f, Interact: interact,
            Arm: arm, SpotCell: cell);
    }
}
