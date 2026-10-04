using System.Text.Json;
using System.Text.Json.Serialization;

namespace TSimulator.Core;

public enum ModuleSlot { Chassis, Drive, Shooter, Magazine, Arm }
public sealed record ModuleParameters
{
    public float? Width { get; init; }
    public float? Depth { get; init; }
    public float? Height { get; init; }
    public float? MassKg { get; init; }
    public float? MaxSpeed { get; init; }
    public float? Acceleration { get; init; }
    public float? TurnSpeed { get; init; }
    public bool? Holonomic { get; init; }
    public float? ShotSpeed { get; init; }
    public float? ShotHeight { get; init; }
    public float? ShotInterval { get; init; }
    public int? MagazineCapacity { get; init; }
    public float? ArmReach { get; init; }
    public float? ArmMinHeight { get; init; }
    public float? ArmMaxHeight { get; init; }
    public RobotSpec Apply(RobotSpec spec) => spec with
    {
        Width=Width??spec.Width, Depth=Depth??spec.Depth, Height=Height??spec.Height, MassKg=MassKg??spec.MassKg,
        MaxSpeed=MaxSpeed??spec.MaxSpeed, Acceleration=Acceleration??spec.Acceleration, TurnSpeed=TurnSpeed??spec.TurnSpeed,
        Holonomic=Holonomic??spec.Holonomic, ShotSpeed=ShotSpeed??spec.ShotSpeed, ShotHeight=ShotHeight??spec.ShotHeight,
        ShotInterval=ShotInterval??spec.ShotInterval, MagazineCapacity=MagazineCapacity??spec.MagazineCapacity,
        ArmReach=ArmReach??spec.ArmReach, ArmMinHeight=ArmMinHeight??spec.ArmMinHeight, ArmMaxHeight=ArmMaxHeight??spec.ArmMaxHeight
    };
}

public sealed record RobotModule(string Id, string Name, ModuleSlot Slot, string Description, float AddedMassKg, ModuleParameters Parameters);
public sealed record ModulePack(int SchemaVersion, RobotModule[] Modules);
public sealed record RobotAssembly
{
    public Dictionary<ModuleSlot,string> Modules { get; init; } = new()
    {
        [ModuleSlot.Chassis]="chassis-standard", [ModuleSlot.Drive]="drive-omni", [ModuleSlot.Shooter]="shooter-standard",
        [ModuleSlot.Magazine]="magazine-30", [ModuleSlot.Arm]="arm-standard"
    };
}
public sealed record RobotPreset(string Id, string Name, RobotSpec Robot);

/// <summary>Declarative, bounded module packs. Simulation/replays store resolved specs and need no catalog at runtime.</summary>
public sealed class RobotModuleCatalog
{
    private static readonly Dictionary<ModuleSlot,string[]> Allowed = new()
    {
        [ModuleSlot.Chassis]=[nameof(ModuleParameters.Width),nameof(ModuleParameters.Depth),nameof(ModuleParameters.Height),nameof(ModuleParameters.MassKg)],
        [ModuleSlot.Drive]=[nameof(ModuleParameters.MaxSpeed),nameof(ModuleParameters.Acceleration),nameof(ModuleParameters.TurnSpeed),nameof(ModuleParameters.Holonomic)],
        [ModuleSlot.Shooter]=[nameof(ModuleParameters.ShotSpeed),nameof(ModuleParameters.ShotHeight),nameof(ModuleParameters.ShotInterval)],
        [ModuleSlot.Magazine]=[nameof(ModuleParameters.MagazineCapacity)],
        [ModuleSlot.Arm]=[nameof(ModuleParameters.ArmReach),nameof(ModuleParameters.ArmMinHeight),nameof(ModuleParameters.ArmMaxHeight)]
    };
    private readonly Dictionary<string,RobotModule> modules = new(StringComparer.Ordinal);
    public IReadOnlyCollection<RobotModule> Modules => modules.Values;
    public RobotModule this[string id] => modules.TryGetValue(id,out var module) ? module : throw new InvalidDataException($"Unknown module: {id}");
    public static RobotModuleCatalog Load(IEnumerable<string> paths)
    {
        var catalog = new RobotModuleCatalog();
        var options = new JsonSerializerOptions(JsonFiles.Options) { UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow };
        var files=paths.Take(129).ToArray();
        if(files.Length>128)throw new InvalidDataException("Too many module packs (maximum 128).");
        foreach (var path in files)
        {
            if (new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("Module pack exceeds 2 MiB.");
            var pack=JsonSerializer.Deserialize<ModulePack>(File.ReadAllText(path),options) ?? throw new InvalidDataException("Empty module pack.");
            if(pack.SchemaVersion!=1 || pack.Modules is null || pack.Modules.Length>256)throw new InvalidDataException("Invalid module pack schema or size.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach (var module in pack.Modules)
            {
                Validate(module);
                if(!ids.Add(module.Id))throw new InvalidDataException($"Duplicate module ID in pack: {module.Id}");
                // Later packs may override a bundled ID, preserving its slot.
                if(catalog.modules.TryGetValue(module.Id,out var prior) && prior.Slot!=module.Slot)
                    throw new InvalidDataException($"Cannot change slot for module: {module.Id}");
                catalog.modules[module.Id]=module;
            }
        }
        return catalog;
    }
    private static void Validate(RobotModule module)
    {
        if(module is null || string.IsNullOrWhiteSpace(module.Id) || module.Id.Length>80 || module.Id.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
            || string.IsNullOrWhiteSpace(module.Name) || module.Name.Length>64 || module.Description is null || module.Description.Length>400
            || !Enum.IsDefined(module.Slot) || !float.IsFinite(module.AddedMassKg) || module.AddedMassKg is < 0 or > 40 || module.Parameters is null)
            throw new InvalidDataException("Invalid module metadata.");
        foreach(var property in typeof(ModuleParameters).GetProperties())
        {
            var value=property.GetValue(module.Parameters);
            if(value is null)continue;
            if(!Allowed[module.Slot].Contains(property.Name,StringComparer.Ordinal))throw new InvalidDataException($"{module.Id}: {property.Name} does not belong to {module.Slot}.");
            if(value is float f && (!float.IsFinite(f) || f<=0))throw new InvalidDataException($"{module.Id}: invalid {property.Name}.");
            if(value is int i && i is < 1 or > 1000)throw new InvalidDataException($"{module.Id}: invalid {property.Name}.");
        }
        module.Parameters.Apply(new()).Validate(new(),sandbox:true);
    }
    public RobotSpec Build(RobotAssembly assembly, string name = "Custom robot")
    {
        if(assembly.Modules is null || assembly.Modules.Count!=Enum.GetValues<ModuleSlot>().Length)throw new InvalidDataException("An assembly needs exactly one module per slot.");
        var robot=new RobotSpec { Name=name }; var addedMass=0f;
        foreach(var slot in Enum.GetValues<ModuleSlot>())
        {
            if(!assembly.Modules.TryGetValue(slot,out var id))throw new InvalidDataException($"Missing module slot: {slot}");
            var module=this[id];
            if(module.Slot!=slot)throw new InvalidDataException($"Module {id} does not fit {slot}.");
            robot=module.Parameters.Apply(robot); addedMass+=module.AddedMassKg;
        }
        robot=robot with { MassKg=robot.MassKg+addedMass, Assembly=assembly with { Modules=new(assembly.Modules) } };
        robot.Validate(new(),sandbox:true);
        return robot;
    }
}
