using System.Text.Json;
using System.Text.Json.Serialization;
namespace TSimulator.Core;

public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static T Load<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Empty configuration: {path}");
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, true);
    }
}
public sealed record RuleProfile
{
    public string Id { get; init; } = "core2-2027";
    public string Version { get; init; } = "V27.2.0";
    public string Source { get; init; } = "https://core.scramble-robot.org/rule/core-2-rulenavi/";
    public int TickRate { get; init; } = 120;
    public int RoundSeconds { get; init; } = 420;
    public int InitialHp { get; init; } = 60;
    public int HitDamage { get; init; } = 10;
    public float MaxShotSpeed { get; init; } = 12;
    public float RespawnSeconds { get; init; } = 30;
    public float InvulnerabilitySeconds { get; init; } = 5;
    public float SkillCooldownSeconds { get; init; } = 5;
    public float PitSeconds { get; init; } = 20;
    public int InitialRp { get; init; } = 100;
    public int InitialDiscs { get; init; } = 200;
    public int KnockoutVp { get; init; } = 10;
    public int DoubleKnockoutVp { get; init; } = 40;
    public int DoubleKnockoutLimit { get; init; } = 2;
    public int FoulVp { get; init; } = 50;
    public int LowerSpotPoints { get; init; } = 30;
    public int UpperSpotPoints { get; init; } = 50;
    public int LineRp { get; init; } = 50;
    public int TimeBonusSeconds { get; init; } = 300;
    public int TimeBonusRp { get; init; } = 50;
    public float DiscRadius { get; init; } = .09f;
    public float DiscThickness { get; init; } = .02f;
    public float PanelSize { get; init; } = .145f;
    public float PanelBottom { get; init; } = .1f;
    public float PanelHitInterval { get; init; } = 1f / 3f;
    public float FieldWidth { get; init; } = 12;
    public float FieldDepth { get; init; } = 8;
    public void Validate()
    {
        float[] finite = [MaxShotSpeed, RespawnSeconds, InvulnerabilitySeconds, SkillCooldownSeconds,
            PitSeconds, DiscRadius, DiscThickness, PanelSize, PanelBottom, PanelHitInterval, FieldWidth, FieldDepth];
        if (finite.Any(x => !float.IsFinite(x) || x < 0) || TickRate is < 30 or > 1000 || RoundSeconds <= 0
            || InitialHp <= 0 || HitDamage <= 0 || MaxShotSpeed is <= 0 or > 100
            || FieldWidth is < 4 or > 200 || FieldDepth is < 4 or > 200 || RespawnSeconds <= 0 || PitSeconds <= 0
            || InitialDiscs < 0 || InitialRp < 0 || TimeBonusSeconds <= 0 || TimeBonusRp < 0
            || KnockoutVp < 0 || DoubleKnockoutVp < 0 || DoubleKnockoutLimit < 0 || FoulVp < 0
            || LowerSpotPoints < 0 || UpperSpotPoints < 0 || LineRp < 0 || PanelSize <= 0 || DiscRadius <= 0 || DiscThickness <= 0)
            throw new InvalidDataException("Invalid rule profile numeric values.");
    }
}
public sealed record RobotSpec
{
    public string Name { get; init; } = "TIMSAH Omni";
    public float Width { get; init; } = .7f;
    public float Depth { get; init; } = .7f;
    public float Height { get; init; } = .8f;
    public float MassKg { get; init; } = 25;
    public float MaxSpeed { get; init; } = 2.5f;
    public float Acceleration { get; init; } = 5;
    public float TurnSpeed { get; init; } = 2;
    public float ShotSpeed { get; init; } = 11.5f;
    public float ShotHeight { get; init; } = .1725f;
    public float ShotInterval { get; init; } = .36f;
    public int MagazineCapacity { get; init; } = 30;
    public float ArmReach { get; init; } = .55f;
    public float ArmMinHeight { get; init; } = .3f;
    public float ArmMaxHeight { get; init; } = .95f;
    public bool Holonomic { get; init; } = true;
    public string? ModelPath { get; init; }
    public float ModelScale { get; init; } = 1;
    public RobotAssembly? Assembly { get; init; }
    public void Validate(RuleProfile rules, bool sandbox)
    {
        float[] numbers = [Width, Depth, Height, MassKg, MaxSpeed, Acceleration, TurnSpeed,
            ShotSpeed, ShotHeight, ShotInterval, ArmReach, ArmMinHeight, ArmMaxHeight, ModelScale];
        if (string.IsNullOrWhiteSpace(Name) || Name.Length>128 || numbers.Any(x => !float.IsFinite(x) || x <= 0) || Width > 2 || Depth > 2 || Height > 3
            || MaxSpeed > 30 || ShotSpeed > 100 || ShotInterval < .01f || MagazineCapacity is < 1 or > 1000
            || ArmMaxHeight < ArmMinHeight || ArmReach > 3)
            throw new InvalidDataException("Invalid robot dimensions or motion parameters.");
        if (!sandbox && (Width > .8f || Depth > .8f || Height > 1 || MassKg > 40
            || ShotSpeed > rules.MaxShotSpeed || ArmMinHeight < .3f || ArmMaxHeight > 1.2f))
            throw new InvalidDataException("Robot exceeds CoRE-2 limits. Use sandbox for experiments.");
    }
}
public sealed record PhysicsSettings
{
    // Empirical parameters, not rulebook constants. Calibrate against recorded throws.
    public float Gravity { get; init; } = 9.80665f;
    public float Drag { get; init; } = .08f;
    public float Lift { get; init; } = .074f;
    public float WindX { get; init; }
    public float WindZ { get; init; }
    public int MaxProjectiles { get; init; } = 2048;
    public void Validate()
    {
        if (!float.IsFinite(Gravity) || Gravity is < 0 or > 30 || !float.IsFinite(Drag) || Drag is < 0 or > 10
            || !float.IsFinite(Lift) || Lift is < 0 or > 10 || !float.IsFinite(WindX) || Math.Abs(WindX) > 100
            || !float.IsFinite(WindZ) || Math.Abs(WindZ) > 100 || MaxProjectiles is < 1 or > 100000)
            throw new InvalidDataException("Invalid physics settings.");
    }
}
public sealed record ObstacleSpec(float X, float Z, float Width, float Depth, float Height, float Yaw = 0);
public sealed record Scenario
{
    public string Name { get; init; } = "CoRE-2 2027 / semifinal";
    public RuleProfile Rules { get; init; } = new();
    public RobotSpec Robot { get; init; } = new();
    public Dictionary<int,RobotSpec> RobotOverrides { get; init; } = [];
    public PhysicsSettings Physics { get; init; } = new();
    public int RobotsPerSide { get; init; } = 2;
    public bool Sandbox { get; init; }
    public bool BotsEnabled { get; init; } = true;
    public bool PreloadContainers { get; init; } = true;
    public bool InfiniteAmmo { get; init; }
    public int RpBonus { get; init; }
    public ObstacleSpec[] ExtraObstacles { get; init; } = [];
    public void Validate()
    {
        Rules.Validate(); Robot.Validate(Rules, Sandbox); Physics.Validate();
        if (RobotsPerSide is < 1 or > 16 || (!Sandbox && RobotsPerSide is not (2 or 3)) || RpBonus is < 0 or > 100000)
            throw new InvalidDataException("Robot count must be 2 or 3 in competition practice (1–16 in sandbox).");
        if (RobotOverrides is null || RobotOverrides.Any(x=>x.Key<0 || x.Key>=RobotsPerSide*2 || x.Value is null))
            throw new InvalidDataException("Invalid robot override target.");
        foreach(var spec in RobotOverrides.Values)spec.Validate(Rules,Sandbox);
        if (ExtraObstacles.Length > 1000 || ExtraObstacles.Any(x => !float.IsFinite(x.X) || !float.IsFinite(x.Z)
            || !float.IsFinite(x.Yaw) || !float.IsFinite(x.Width) || x.Width <= 0
            || !float.IsFinite(x.Depth) || x.Depth <= 0 || !float.IsFinite(x.Height) || x.Height <= 0))
            throw new InvalidDataException("Invalid obstacles.");
    }
}
public sealed record AppSettings
{
    public int TargetFps { get; set; } = 144;
    public float CameraSensitivity { get; set; } = 1.5f;
    public float Fov { get; set; } = 70;
    public bool ShowMinimap { get; set; } = true;
    public Dictionary<string, string> Keys { get; set; } = new()
    {
        ["Forward"] = "W", ["Backward"] = "S", ["Left"] = "A", ["Right"] = "D",
        ["TurnLeft"] = "Q", ["TurnRight"] = "E", ["CameraUp"] = "Up", ["CameraDown"] = "Down",
        ["CameraLeft"] = "Left", ["CameraRight"] = "Right", ["Fire"] = "Space",
        ["Interact"] = "F", ["Drop"] = "G", ["ArmUp"] = "PageUp", ["ArmDown"] = "PageDown",
        ["CameraMode"] = "C", ["SwitchRobot"] = "Tab", ["Pause"] = "P", ["Reset"] = "R",
        ["PitIn"] = "One", ["Supply"] = "Two", ["Healing1"] = "Three", ["Healing2"] = "Four",
        ["Boost1"] = "Five", ["Boost2"] = "Six", ["Barrier1"] = "Seven", ["Barrier2"] = "Eight", ["Regenerate"] = "Nine",
        ["Settings"] = "F1", ["EmergencyStop"] = "X", ["Record"] = "F9", ["ReloadConfig"] = "F5"
    };
    public void Validate()
    {
        if (TargetFps is < 30 or > 360 || !float.IsFinite(CameraSensitivity) || CameraSensitivity is < .1f or > 5
            || !float.IsFinite(Fov) || Fov is < 35 or > 110 || Keys is null)
            throw new InvalidDataException("Invalid application settings.");
        foreach (var key in new AppSettings().Keys.Keys)
            if (!Keys.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"Missing key binding: {key}");
        if (Keys.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Keys.Count)
            throw new InvalidDataException("Key bindings must be unique.");
    }
}
