using System.Numerics;
using System.Security.Cryptography;
namespace TSimulator.Core;

public enum Side { Red, Blue }
public enum Skill { None, PitIn, Supply, Healing1, Healing2, Boost1, Boost2, Barrier1, Barrier2, Regenerate }
public readonly record struct RobotCommand(int RobotId, float Forward = 0, float Strafe = 0, float Turn = 0,
    bool Fire = false, bool Interact = false, bool Drop = false, float Arm = 0,
    float AimYaw = 0, float AimPitch = 0, int SpotCell = -1, Skill Skill = Skill.None, bool EmergencyStop = false);
public sealed class Robot(int id, Side side, RobotSpec spec, Vector3 position, float yaw, int hp)
{
    public int Id { get; } = id;
    public Side Side { get; } = side;
    public RobotSpec Spec { get; } = spec;
    public Vector3 Position { get; internal set; } = position;
    public Vector3 PreviousPosition { get; internal set; } = position;
    public Vector3 Velocity { get; internal set; }
    public float Yaw { get; internal set; } = yaw;
    public float PreviousYaw { get; internal set; } = yaw;
    public int Hp { get; internal set; } = hp;
    public bool Alive => Hp > 0;
    public bool Stopped { get; internal set; }
    public int Ammo { get; internal set; }
    public int? ContainerId { get; internal set; }
    public float ArmHeight { get; internal set; } = spec.ArmMinHeight;
    public long RespawnAt { get; internal set; } = long.MaxValue;
    public long InvulnerableUntil { get; internal set; }
    public long NextShotAt { get; internal set; }
    public long[] NextPanelHit { get; } = new long[4];
    public long PitUntil { get; internal set; }
    public bool PitAuthorized { get; internal set; }
    public bool PitVisit { get; internal set; }
    public Box Body => new(Position + new Vector3(0, Spec.Height / 2, 0), new(Spec.Depth / 2, Spec.Height / 2, Spec.Width / 2), Yaw);
    public Vector3 Forward => new(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));
    public Vector3 Gripper => Position + Forward * (Spec.Depth / 2 + Spec.ArmReach) + new Vector3(0, ArmHeight + .06f, 0);
}
public sealed class Alliance(int rp, int reserve)
{
    public int Vp { get; internal set; }
    public int Rp { get; internal set; } = rp;
    public int Reserve { get; internal set; } = reserve;
    public int Knockouts { get; internal set; }
    public int Damage { get; internal set; }
    public int DoubleBonuses { get; internal set; }
    public bool DoubleLatched { get; internal set; }
    public int Fouls { get; internal set; }
    public int SpotMask { get; internal set; }
    public int LineMask { get; internal set; }
    public long SkillReadyAt { get; internal set; }
    public long EffectUntil { get; internal set; }
    public Skill Effect { get; internal set; }
    public int SpotCount => BitOperations.PopCount((uint)SpotMask);
}
public sealed class Container(int id, Side? side, Vector3 position)
{
    public int Id { get; } = id;
    public Side? Side { get; } = side;
    public Vector3 Position { get; internal set; } = position;
    public int? HeldBy { get; internal set; }
    public bool Deposited { get; internal set; }
}
public struct Projectile
{
    public int OwnerId;
    public Vector3 Position;
    public Vector3 PreviousPosition;
    public Vector3 Velocity;
    public float Age;
}
public sealed record SimEvent(long Tick, string Kind, string Message);
public sealed record RoundResult(Side? Winner, string Reason);

public sealed class Simulation
{
    private readonly List<Robot> robots = [];
    private readonly List<Container> containers = [];
    private readonly List<Projectile> projectiles;
    private readonly Queue<SimEvent> events = new();
    private bool timeBonus;
    public Scenario Scenario { get; }
    public RuleProfile Rules => Scenario.Rules;
    public long Tick { get; private set; }
    public double Elapsed => (double)Tick / Rules.TickRate;
    public float DeltaTime => 1f / Rules.TickRate;
    public bool Finished => Tick >= (long)Rules.RoundSeconds * Rules.TickRate;
    public IReadOnlyList<Robot> Robots => robots;
    public IReadOnlyList<Container> Containers => containers;
    public IReadOnlyList<Projectile> Projectiles => projectiles;
    public IReadOnlyCollection<SimEvent> Events => events;
    public Alliance[] Alliances { get; }
    public Box[] Obstacles { get; }
    public Simulation(Scenario scenario)
    {
        scenario.Validate(); Scenario = scenario; Obstacles = Arena.Obstacles(scenario);
        projectiles = new List<Projectile>(Math.Min(scenario.Physics.MaxProjectiles, 4096));
        Alliances = [new(Rules.InitialRp + scenario.RpBonus, Rules.InitialDiscs), new(Rules.InitialRp + scenario.RpBonus, Rules.InitialDiscs)];
        for (var side = 0; side < 2; side++)
        {
            for (var i = 0; i < scenario.RobotsPerSide; i++)
            {
                var x = .6f + (i % 3) * .85f; var z = .6f + (i / 3) * .85f;
                if (!scenario.Sandbox && i == 2) { x = .6f; z = 1.5f; }
                var p = side == 0 ? new Vector3(x, .011f, z) : new Vector3(Rules.FieldWidth - x, .011f, Rules.FieldDepth - z);
                var robot = new Robot(robots.Count, (Side)side, scenario.Robot, p, side == 0 ? 0 : MathF.PI, Rules.InitialHp);
                robot.Ammo = Math.Min(scenario.Robot.MagazineCapacity, Alliances[side].Reserve);
                Alliances[side].Reserve -= robot.Ammo;
                robots.Add(robot);
                if (scenario.PreloadContainers)
                {
                    var container = new Container(containers.Count, (Side)side, robot.Gripper) { HeldBy = robot.Id };
                    containers.Add(container); robot.ContainerId = container.Id;
                }
            }
            for (var i = 0; i < 5; i++)
            {
                var z = 5.69f + i * .24f - (i >= 3 ? .04f : 0);
                var p = side == 0 ? new Vector3(1.86f, i < 3 ? .36f : .51f, z) : new Vector3(10.14f, i < 3 ? .36f : .51f, 8 - z);
                containers.Add(new(containers.Count, (Side)side, p));
            }
        }
        foreach (var p in new Vector3[] { new(5.1303f,.51f,4.7f), new(5.3f,.51f,4.8697f), new(6.7f,.51f,3.1303f), new(6.8697f,.51f,3.3f) })
            containers.Add(new(containers.Count, null, p));
        Log("round", "CoRE-2 " + Rules.Version);
    }
    public Alliance Team(Side side) => Alliances[(int)side];
    public static Side Opponent(Side side) => side == Side.Red ? Side.Blue : Side.Red;
    public long Ticks(float seconds) => (long)Math.Ceiling((double)seconds * Rules.TickRate - .000001);
    private void Log(string kind, string message)
    {
        events.Enqueue(new(Tick, kind, message));
        while (events.Count > 64) events.Dequeue();
    }
    public void Step(ReadOnlySpan<RobotCommand> commands)
    {
        if (Finished) return;
        foreach (var robot in robots) { robot.PreviousPosition = robot.Position; robot.PreviousYaw = robot.Yaw; }
        foreach (var robot in robots)
        {
            if (!robot.Alive && !robot.Stopped && Tick >= robot.RespawnAt) Revive(robot);
            if (robot.PitUntil > 0 && Tick >= robot.PitUntil)
            {
                var team = Team(robot.Side); var load = Math.Min(robot.Spec.MagazineCapacity - robot.Ammo, team.Reserve);
                robot.Ammo += load; team.Reserve -= load; robot.PitUntil = 0;
                Log("pit", $"{robot.Id + 1}: pit complete / +{load} discs");
            }
        }
        foreach (var command in commands)
        {
            if (command.RobotId < 0 || command.RobotId >= robots.Count) continue;
            if (!float.IsFinite(command.Forward) || !float.IsFinite(command.Strafe) || !float.IsFinite(command.Turn)
                || !float.IsFinite(command.Arm) || !float.IsFinite(command.AimYaw) || !float.IsFinite(command.AimPitch)) continue;
            var robot = robots[command.RobotId];
            if (command.EmergencyStop) { robot.Stopped = true; robot.Velocity = Vector3.Zero; Log("stop", $"{robot.Id + 1}: emergency stop"); }
            if (command.Skill != Skill.None) ActivateSkill(robot.Side, command.Skill, robot.Id);
            if (!robot.Alive || robot.Stopped || robot.PitUntil > Tick) continue;
            Move(robot, command);
            if (command.Fire) Fire(robot, command.AimYaw, command.AimPitch);
            if (command.Interact) Interact(robot, command.SpotCell);
            if (command.Drop) Drop(robot);
        }
        foreach (var robot in robots)
        {
            if (robot.ContainerId is int id) containers[id].Position = robot.Gripper;
            if (!robot.Alive || robot.Stopped) robot.Velocity = Vector3.Zero;
            if (robot.PitAuthorized && !robot.PitVisit && robot.Alive && Arena.InPitZone(robot.Side, robot.Body)
                && !robots.Any(x => x.Id != robot.Id && x.Side == robot.Side && x.PitVisit))
            {
                robot.PitAuthorized = false; robot.PitVisit = true; robot.PitUntil = Tick + Ticks(Rules.PitSeconds);
                robot.Velocity = Vector3.Zero; Log("pit", $"{robot.Id + 1}: pit in / {Rules.PitSeconds}s");
            }
            if (robot.PitVisit && robot.PitUntil == 0 && !Arena.InPitArea(robot.Side, new(robot.Position.X, robot.Position.Z))) robot.PitVisit = false;
        }
        IntegrateProjectiles();
        foreach (var side in new[] { Side.Red, Side.Blue })
        {
            var team = Team(side); var down = robots.Count(r => r.Side == Opponent(side) && !r.Alive);
            if (down >= 2 && !team.DoubleLatched && team.DoubleBonuses < Rules.DoubleKnockoutLimit)
            {
                team.Vp += Rules.DoubleKnockoutVp; team.DoubleBonuses++; Log("vp", $"{side}: double knockout +{Rules.DoubleKnockoutVp}VP");
            }
            team.DoubleLatched = down >= 2;
        }
        Tick++;
        if (!timeBonus && Tick >= (long)Rules.TimeBonusSeconds * Rules.TickRate)
        {
            foreach (var team in Alliances) team.Rp += Rules.TimeBonusRp;
            timeBonus = true; Log("rp", $"time bonus +{Rules.TimeBonusRp}RP");
        }
        if (Finished) Log("end", Result().Reason);
    }
    private bool Fits(Robot robot, Vector3 position, float yaw)
    {
        var body = new Box(position + new Vector3(0, robot.Spec.Height / 2, 0), robot.Body.HalfSize, yaw);
        var c = Math.Abs(MathF.Cos(yaw)); var s = Math.Abs(MathF.Sin(yaw));
        var ex = body.HalfSize.X * c + body.HalfSize.Z * s; var ez = body.HalfSize.X * s + body.HalfSize.Z * c;
        if (position.X - ex < 0 || position.X + ex > Rules.FieldWidth || position.Z - ez < 0 || position.Z + ez > Rules.FieldDepth) return false;
        foreach (var obstacle in Obstacles) if (body.Intersects(obstacle)) return false;
        foreach (var other in robots) if (other.Id != robot.Id && body.Intersects(other.Body)) return false;
        // Keep robot out of opponent pit. Own pit is authorized for one visit per skill use.
        foreach (var p in body.Corners())
        {
            if (Arena.InPitArea(Opponent(robot.Side), p)) return false;
            if (Arena.InPitArea(robot.Side, p) && !robot.PitAuthorized && !robot.PitVisit) return false;
        }
        return true;
    }
    private void Move(Robot robot, RobotCommand command)
    {
        var dt = DeltaTime;
        var yaw = Wrap(robot.Yaw + Math.Clamp(command.Turn, -1, 1) * robot.Spec.TurnSpeed * dt);
        if (Fits(robot, robot.Position, yaw)) robot.Yaw = yaw;
        var f = robot.Forward; var right = new Vector3(-f.Z, 0, f.X);
        var move = f * Math.Clamp(command.Forward, -1, 1) + right * (robot.Spec.Holonomic ? Math.Clamp(command.Strafe, -1, 1) : 0);
        if (move.LengthSquared() > 1) move = Vector3.Normalize(move);
        var desired = move * robot.Spec.MaxSpeed; var difference = desired - robot.Velocity;
        robot.Velocity += difference.LengthSquared() > MathF.Pow(robot.Spec.Acceleration * dt, 2)
            ? Vector3.Normalize(difference) * robot.Spec.Acceleration * dt : difference;
        var next = robot.Position + robot.Velocity * dt;
        // Axis separation allows sliding along walls without penetrating their OBBs.
        var nx = new Vector3(next.X, robot.Position.Y, robot.Position.Z);
        if (Fits(robot, nx, robot.Yaw)) robot.Position = nx; else robot.Velocity = new(0, 0, robot.Velocity.Z);
        var nz = new Vector3(robot.Position.X, robot.Position.Y, next.Z);
        if (Fits(robot, nz, robot.Yaw)) robot.Position = nz; else robot.Velocity = new(robot.Velocity.X, 0, 0);
        robot.ArmHeight = Math.Clamp(robot.ArmHeight + Math.Clamp(command.Arm, -1, 1) * .4f * dt, robot.Spec.ArmMinHeight, robot.Spec.ArmMaxHeight);
    }
    public static float Wrap(float angle) => MathF.IEEERemainder(angle, MathF.Tau);
    private void Fire(Robot robot, float aimYaw, float aimPitch)
    {
        if (Tick < robot.NextShotAt || (!Scenario.InfiniteAmmo && robot.Ammo <= 0) || projectiles.Count >= Scenario.Physics.MaxProjectiles) return;
        var yaw = robot.Yaw + Math.Clamp(aimYaw, -.8f, .8f); var pitch = Math.Clamp(aimPitch, -.3f, .5f);
        var direction = new Vector3(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Sin(yaw) * MathF.Cos(pitch));
        var muzzle = robot.Position + new Vector3(0, robot.Spec.ShotHeight, 0) + direction * (robot.Spec.Depth / 2 + Rules.DiscRadius + .01f);
        projectiles.Add(new() { OwnerId = robot.Id, Position = muzzle, PreviousPosition = muzzle,
            Velocity = direction * robot.Spec.ShotSpeed });
        robot.NextShotAt = Tick + Ticks(robot.Spec.ShotInterval);
        if (!Scenario.InfiniteAmmo) robot.Ammo--;
    }
    public Box Panel(Robot robot, int index)
    {
        var y = robot.Position.Y + Rules.PanelBottom + Rules.PanelSize / 2;
        var angle = robot.Yaw + index * MathF.PI / 2;
        var distance = index % 2 == 0 ? robot.Spec.Depth / 2 : robot.Spec.Width / 2;
        return new(robot.Position + new Vector3(MathF.Cos(angle) * (distance + .006f), y - robot.Position.Y,
            MathF.Sin(angle) * (distance + .006f)), new(.007f, Rules.PanelSize / 2, Rules.PanelSize / 2), angle);
    }
    private void IntegrateProjectiles()
    {
        var dt = DeltaTime; var physics = Scenario.Physics;
        var expansion = new Vector3(Rules.DiscRadius, Rules.DiscThickness / 2, Rules.DiscRadius);
        for (var i = projectiles.Count - 1; i >= 0; i--)
        {
            var p = projectiles[i]; p.PreviousPosition = p.Position;
            var air = p.Velocity - new Vector3(physics.WindX, 0, physics.WindZ);
            var horizontalSq = air.X * air.X + air.Z * air.Z;
            var acceleration = -physics.Drag * air.Length() * air + new Vector3(0, -physics.Gravity + physics.Lift * horizontalSq, 0);
            p.Velocity += acceleration * dt; p.Position += p.Velocity * dt; p.Age += dt;
            float first = 2; Robot? hitRobot = null; var hitPanel = -1;
            foreach (var obstacle in Obstacles)
                if (obstacle.Sweep(p.PreviousPosition, p.Position, expansion, out var t) && t < first) first = t;
            foreach (var robot in robots)
            {
                if (robot.Id == p.OwnerId && p.Age < .1f) continue;
                var bodyHit = robot.Body.Sweep(p.PreviousPosition, p.Position, expansion, out var bodyT);
                // Panels protrude from the chassis. Only contact with a panel causes damage.
                for (var panel = 0; panel < 4; panel++)
                {
                    var box = Panel(robot, panel);
                    var normal = new Vector3(MathF.Cos(box.Yaw), 0, MathF.Sin(box.Yaw));
                    if (Vector3.Dot(p.Velocity, normal) >= 0) continue;
                    if (box.Sweep(p.PreviousPosition, p.Position, expansion, out var t) && t < first && (!bodyHit || t <= bodyT + .002f))
                    { first = t; hitRobot = robot; hitPanel = panel; }
                }
                if (bodyHit && bodyT < first - .002f) { first = bodyT; hitRobot = null; hitPanel = -1; }
            }
            if (hitRobot is not null && hitPanel >= 0 && Tick >= hitRobot.NextPanelHit[hitPanel])
            {
                ApplyHit(robots[p.OwnerId].Side, hitRobot.Id);
                hitRobot.NextPanelHit[hitPanel] = Tick + Ticks(Rules.PanelHitInterval);
            }
            if (first <= 1 || p.Position.Y <= .011f + Rules.DiscThickness / 2 || p.Age > 12
                || p.Position.X < 0 || p.Position.Z < 0 || p.Position.X > Rules.FieldWidth || p.Position.Z > Rules.FieldDepth)
            { projectiles[i] = projectiles[^1]; projectiles.RemoveAt(projectiles.Count - 1); }
            else projectiles[i] = p;
        }
    }
    public void ApplyHit(Side shooter, int robotId)
    {
        if (Finished || robotId < 0 || robotId >= robots.Count) return;
        var robot = robots[robotId]; var defending = Team(robot.Side); var attacking = Team(shooter);
        if (!robot.Alive || robot.Stopped || Tick < robot.InvulnerableUntil
            || (defending.Effect == Skill.Barrier2 && Tick < defending.EffectUntil)) return;
        var damage = Rules.HitDamage;
        if (attacking.Effect is Skill.Boost1 or Skill.Boost2 && Tick < attacking.EffectUntil) damage *= 2;
        if (defending.Effect == Skill.Barrier1 && Tick < defending.EffectUntil) damage /= 2;
        damage = Math.Min(damage, robot.Hp); robot.Hp -= damage;
        if (shooter != robot.Side) attacking.Damage += damage;
        Log("hit", $"{robot.Id + 1}: -{damage}HP");
        if (!robot.Alive)
        {
            robot.RespawnAt = Tick + Ticks(Rules.RespawnSeconds); robot.Velocity = Vector3.Zero;
            // Knockout belongs to the opposing alliance, including friendly-fire knockouts.
            var winner = Team(Opponent(robot.Side)); winner.Vp += Rules.KnockoutVp; winner.Knockouts++;
            Log("ko", $"{robot.Id + 1}: knockout / {Rules.RespawnSeconds}s");
        }
    }
    private void Revive(Robot robot)
    {
        robot.Hp = Rules.InitialHp; robot.RespawnAt = long.MaxValue;
        robot.InvulnerableUntil = Tick + Ticks(Rules.InvulnerabilitySeconds);
        Log("revive", $"{robot.Id + 1}: revived");
    }
    public bool ActivateSkill(Side side, Skill skill, int targetId = -1)
    {
        if (Finished || skill == Skill.None || !Enum.IsDefined(skill)) return false;
        var team = Team(side); var exempt = skill is Skill.PitIn or Skill.Supply;
        var cost = skill switch { Skill.PitIn => 20, Skill.Supply => 100, Skill.Healing1 => 50, Skill.Healing2 => 100,
            Skill.Boost1 => 50, Skill.Boost2 => 90, Skill.Barrier1 => 80, Skill.Barrier2 => 150, Skill.Regenerate => 100, _ => int.MaxValue };
        if (team.Rp < cost || (!exempt && Tick < team.SkillReadyAt)) return false;
        Robot? target = targetId >= 0 && targetId < robots.Count ? robots[targetId] : null;
        if (skill == Skill.PitIn && (target is null || target.Side != side || !target.Alive || target.Stopped || target.PitAuthorized || target.PitVisit)) return false;
        if (skill == Skill.Regenerate)
        {
            target = target is not null && target.Side == side && !target.Alive && !target.Stopped
                ? target : robots.FirstOrDefault(r => r.Side == side && !r.Alive && !r.Stopped);
            if (target is null) return false;
        }
        team.Rp -= cost;
        var duration = skill switch { Skill.Boost1 or Skill.Barrier1 => 20, Skill.Boost2 => 40, Skill.Barrier2 => 15, _ => 0 };
        if (!exempt)
        {
            team.Effect = skill; team.EffectUntil = Tick + Ticks(duration);
            team.SkillReadyAt = team.EffectUntil + Ticks(Rules.SkillCooldownSeconds);
        }
        switch (skill)
        {
            case Skill.PitIn: target!.PitAuthorized = true; break;
            case Skill.Supply: team.Reserve += 100; break;
            case Skill.Healing1:
            case Skill.Healing2:
                foreach (var robot in robots.Where(r => r.Side == side && r.Alive))
                    robot.Hp = Math.Min(Rules.InitialHp, robot.Hp + (skill == Skill.Healing1 ? 30 : Rules.InitialHp));
                break;
            case Skill.Regenerate: Revive(target!); break;
        }
        Log("skill", $"{side}: {skill} / -{cost}RP"); return true;
    }
    public void Foul(Side side, string reason)
    {
        if (Finished) return;
        var team = Team(side); team.Vp -= Rules.FoulVp; team.Fouls++; Log("foul", $"{side}: -{Rules.FoulVp}VP / {reason}");
    }
    private void Drop(Robot robot)
    {
        if (robot.ContainerId is not int id) return;
        containers[id].HeldBy = null; robot.ContainerId = null;
        // Ground-dropped containers cannot be picked up by an arm constrained above 300mm.
        containers[id].Position = new(robot.Gripper.X, .071f, robot.Gripper.Z);
    }
    private void Interact(Robot robot, int cell)
    {
        if (robot.ContainerId is int id)
        {
            var candidates = cell >= 0 && cell < 9 ? new[] { cell } : Enumerable.Range(0, 9)
                .OrderBy(c => Vector3.DistanceSquared(robot.Gripper, Arena.SpotPosition(robot.Side, c))).ToArray();
            foreach (var c in candidates)
            {
                var goal = Arena.SpotPosition(robot.Side, c);
                if (Vector3.Distance(robot.Gripper, goal) <= .28f && Math.Abs(robot.Gripper.Y - goal.Y) <= .13f)
                {
                    if (Deposit(robot.Side, c, id)) robot.ContainerId = null;
                    return;
                }
            }
            return;
        }
        Container? nearest = null; var distance = .24f * .24f;
        foreach (var container in containers)
        {
            if (container.Deposited || container.HeldBy.HasValue || (container.Side.HasValue && container.Side != robot.Side)) continue;
            var d = Vector3.DistanceSquared(container.Position, robot.Gripper);
            if (d < distance && container.Position.Y - .06f >= robot.Spec.ArmMinHeight - .015f) { nearest = container; distance = d; }
        }
        if (nearest is not null) { nearest.HeldBy = robot.Id; robot.ContainerId = nearest.Id; Log("pickup", $"{robot.Id + 1}: container"); }
    }
    public bool Deposit(Side side, int cell, int containerId)
    {
        if (Finished || cell is < 0 or > 8 || containerId < 0 || containerId >= containers.Count) return false;
        var container = containers[containerId]; var team = Team(side);
        if (container.Deposited || (container.Side.HasValue && container.Side != side)) return false;
        if ((team.SpotMask & (1 << cell)) != 0) return false;
        team.SpotMask |= 1 << cell;
        container.Deposited = true; container.Position = Arena.SpotPosition(side, cell);
        if (container.HeldBy is int holder) robots[holder].ContainerId = null;
        container.HeldBy = null;
        var reward = cell < 3 ? Rules.LowerSpotPoints : Rules.UpperSpotPoints;
        team.Vp += reward; team.Rp += reward;
        for (var i = 0; i < Arena.Lines.Length; i++)
        {
            var mask = Arena.Lines[i].Aggregate(0, (m, c) => m | (1 << c));
            if ((team.SpotMask & mask) == mask && (team.LineMask & (1 << i)) == 0)
            { team.LineMask |= 1 << i; team.Rp += Rules.LineRp; }
        }
        Log("spot", $"{side}: spot {cell + 1} / +{reward}VP +{reward}RP"); return true;
    }
    public RoundResult Result()
    {
        var red = Alliances[0]; var blue = Alliances[1];
        foreach (var (a, b, label) in new[] { (red.Vp, blue.Vp, "VP"), (red.SpotCount, blue.SpotCount, "containers"),
            (red.Knockouts, blue.Knockouts, "knockouts"), (red.Damage, blue.Damage, "damage") })
            if (a != b) return new(a > b ? Side.Red : Side.Blue, $"{(a > b ? "Red" : "Blue")} wins / {label}");
        return new(null, "Referee decision required (all tie-breakers equal)");
    }
    public string StateHash()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        void Vector(Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        w.Write(Tick); w.Write(timeBonus);
        foreach (var t in Alliances)
        {
            w.Write(t.Vp); w.Write(t.Rp); w.Write(t.Reserve); w.Write(t.Knockouts); w.Write(t.Damage);
            w.Write(t.DoubleBonuses); w.Write(t.DoubleLatched); w.Write(t.Fouls); w.Write(t.SpotMask); w.Write(t.LineMask);
            w.Write(t.SkillReadyAt); w.Write(t.EffectUntil); w.Write((int)t.Effect);
        }
        foreach (var r in robots)
        {
            Vector(r.Position); Vector(r.Velocity); w.Write(r.Yaw); w.Write(r.Hp); w.Write(r.Stopped); w.Write(r.Ammo);
            w.Write(r.ContainerId ?? -1); w.Write(r.ArmHeight); w.Write(r.RespawnAt); w.Write(r.InvulnerableUntil);
            w.Write(r.NextShotAt); w.Write(r.PitUntil); w.Write(r.PitAuthorized); w.Write(r.PitVisit);
            foreach (var t in r.NextPanelHit) w.Write(t);
        }
        foreach (var c in containers) { Vector(c.Position); w.Write(c.HeldBy ?? -1); w.Write(c.Deposited); }
        w.Write(projectiles.Count);
        foreach (var p in projectiles) { w.Write(p.OwnerId); Vector(p.Position); Vector(p.Velocity); w.Write(p.Age); }
        w.Flush(); return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
    }
}
