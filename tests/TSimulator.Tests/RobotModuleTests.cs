using System.Text.Json;
using TSimulator.Core;

namespace TSimulator.Tests;

public class RobotModuleTests
{
    private static string Config => Path.Combine(AppContext.BaseDirectory,"config");
    private static RobotModuleCatalog Catalog() => RobotModuleCatalog.Load(Directory.GetFiles(Path.Combine(Config,"modules"),"*.json"));
    [Fact] public void BuiltInModulesReproduceStandardRobotAndSupportRapidLargeMagazine()
    {
        var catalog=Catalog();var standard=catalog.Build(new());
        Assert.Equal(25,standard.MassKg);Assert.Equal(.36f,standard.ShotInterval);Assert.Equal(30,standard.MagazineCapacity);
        var assembly=new RobotAssembly();assembly.Modules[ModuleSlot.Shooter]="shooter-rapid";assembly.Modules[ModuleSlot.Magazine]="magazine-90";
        var rapid=catalog.Build(assembly);rapid.Validate(new(),false);
        Assert.Equal(.08f,rapid.ShotInterval);Assert.Equal(90,rapid.MagazineCapacity);Assert.Equal(27,rapid.MassKg);
    }
    [Fact] public void EveryStarterPresetHasValidSnapshotAndModuleRecipe()
    {
        var catalog=Catalog();var files=Directory.GetFiles(Path.Combine(Config,"robots"),"*.json");Assert.Equal(6,files.Length);
        foreach(var file in files)
        {
            var preset=JsonFiles.Load<RobotPreset>(file);preset.Robot.Validate(new(),true);
            var built=catalog.Build(preset.Robot.Assembly!,preset.Robot.Name);
            Assert.Equal(preset.Robot.ShotInterval,built.ShotInterval);Assert.Equal(preset.Robot.MagazineCapacity,built.MagazineCapacity);
            Assert.Equal(preset.Robot.MassKg,built.MassKg,3);
        }
    }
    [Fact] public void ExperimentalModuleRequiresSandbox()
    {
        var assembly=new RobotAssembly();assembly.Modules[ModuleSlot.Shooter]="shooter-experimental";
        var robot=Catalog().Build(assembly);
        Assert.Throws<InvalidDataException>(()=>robot.Validate(new(),false));robot.Validate(new(),true);
    }
    [Fact] public void MissingSlotOrWrongModuleCategoryCannotBuild()
    {
        var catalog=Catalog();var missing=new RobotAssembly();missing.Modules.Remove(ModuleSlot.Arm);
        Assert.Throws<InvalidDataException>(()=>catalog.Build(missing));
        var wrong=new RobotAssembly();wrong.Modules[ModuleSlot.Shooter]="drive-omni";
        Assert.Throws<InvalidDataException>(()=>catalog.Build(wrong));
    }
    [Fact] public void RobotSpecificPresetsPreserveOtherRobotsAndShareReserveCorrectly()
    {
        var sim=new Simulation(new() { RobotOverrides=new() { [0]=new() { MagazineCapacity=90,ShotInterval=.08f },[2]=new() { MagazineCapacity=60 } } });
        Assert.Equal(90,sim.Robots[0].Ammo);Assert.Equal(30,sim.Robots[1].Ammo);Assert.Equal(.08f,sim.Robots[0].Spec.ShotInterval);
        Assert.Equal(80,sim.Team(Side.Red).Reserve);Assert.Equal(110,sim.Team(Side.Blue).Reserve);
        Assert.Throws<InvalidDataException>(()=>new Simulation(new() { RobotOverrides=new() { [4]=new() } }));
    }
    [Fact] public void LargeMagazineDoesNotInventInitialDiscs()
    {
        var sim=new Simulation(new() { Robot=new() { MagazineCapacity=150 } });
        Assert.Equal(150,sim.Robots[0].Ammo);Assert.Equal(50,sim.Robots[1].Ammo);Assert.Equal(0,sim.Team(Side.Red).Reserve);
    }
    [Fact] public void CustomPackCanOverrideShooterButCannotChangeItsSlot()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try
        {
            var module=new RobotModule("shooter-standard","Custom",ModuleSlot.Shooter,"Local pack",1,new() { ShotInterval=.12f });
            JsonFiles.Save(path,new ModulePack(1,[module]));
            var catalog=RobotModuleCatalog.Load(Directory.GetFiles(Path.Combine(Config,"modules"),"*.json").Append(path));
            Assert.Equal(.12f,catalog.Build(new()).ShotInterval);
            JsonFiles.Save(path,new ModulePack(1,[module with { Slot=ModuleSlot.Drive,Parameters=new() { MaxSpeed=2 } }]));
            Assert.Throws<InvalidDataException>(()=>RobotModuleCatalog.Load(Directory.GetFiles(Path.Combine(Config,"modules"),"*.json").Append(path)));
        }
        finally { File.Delete(path); }
    }
    [Fact] public void MisspelledModuleParameterIsAnError()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try
        {
            File.WriteAllText(path,"""{"schemaVersion":1,"modules":[{"id":"test","name":"test","slot":"Shooter","description":"test","addedMassKg":0,"parameters":{"shotIntervel":0.1}}]}""");
            Assert.Throws<JsonException>(()=>RobotModuleCatalog.Load([path]));
        }
        finally { File.Delete(path); }
    }
    [Fact] public void ResolvedRobotRecipeReplaysWithoutCatalogDependency()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".jsonl");
        try
        {
            var assembly=new RobotAssembly();assembly.Modules[ModuleSlot.Shooter]="shooter-rapid";
            var scenario=new Scenario { RobotOverrides=new() { [0]=Catalog().Build(assembly) } };
            var sim=new Simulation(scenario);
            using(var writer=new ReplayWriter(path,scenario))
            {
                for(var i=0;i<240;i++){RobotCommand[] commands=[new(0,Fire:true,Turn:.3f)];writer.Write(commands);sim.Step(commands);}
                writer.Complete(sim);
            }
            var replay=Replay.Play(path);Assert.Equal(sim.StateHash(),replay.StateHash());Assert.Equal(.08f,replay.Robots[0].Spec.ShotInterval);
        }
        finally { File.Delete(path); }
    }
}
