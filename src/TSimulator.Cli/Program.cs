using System.Diagnostics;
using System.Text.Json;
using TSimulator.Core;

try
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("TSimulator .NET 10\n  simulate [--scenario FILE] [--seconds N] [--record FILE]\n  benchmark [--scenario FILE] [--seconds N]\n  replay FILE\n  assets fetch DIRECTORY\n  assets import HTML DIRECTORY\n  validate FILE\n  defaults DIRECTORY");
        return 0;
    }
    string? Option(string key) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    var verb = args[0];
    if (verb == "assets")
    {
        if (args.Length == 4 && args[1] == "import") OfficialAssets.Import(File.ReadAllText(args[2]), args[3]);
        else if (args.Length == 3 && args[1] == "fetch") await OfficialAssets.FetchAsync(args[2]);
        else throw new ArgumentException("Use: assets fetch DIRECTORY / assets import HTML DIRECTORY");
        Console.WriteLine("Official CAD imported (local cache). Source provenance recorded."); return 0;
    }
    if (verb == "defaults")
    {
        var dir = args[1];
        JsonFiles.Save(Path.Combine(dir, "core2-semifinal.json"), new Scenario());
        JsonFiles.Save(Path.Combine(dir, "core2-final.json"), new Scenario { Name = "CoRE-2 2027 / final", RobotsPerSide = 3, Rules = new() { InitialDiscs = 300 } });
        JsonFiles.Save(Path.Combine(dir, "sandbox.json"), new Scenario { Name = "Free practice", Sandbox = true, InfiniteAmmo = true, RobotsPerSide = 2, PreloadContainers = false });
        JsonFiles.Save(Path.Combine(dir, "settings.json"), new AppSettings()); return 0;
    }
    if (verb == "validate") { JsonFiles.Load<Scenario>(args[1]).Validate(); Console.WriteLine("Scenario valid."); return 0; }
    if (verb == "replay")
    {
        var replayed = Replay.Play(args[1]); Console.WriteLine($"Verified {replayed.Tick} ticks: {replayed.StateHash()}"); return 0;
    }
    if (verb is not ("simulate" or "benchmark")) throw new ArgumentException("Unknown command.");
    var scenario = Option("--scenario") is string path ? JsonFiles.Load<Scenario>(path) : new Scenario();
    var sim = new Simulation(scenario); var bot = new PracticeBot();
    var commands = new RobotCommand[sim.Robots.Count];
    var seconds = int.Parse(Option("--seconds") ?? "420", System.Globalization.CultureInfo.InvariantCulture);
    if (seconds is < 1 or > 86400) throw new ArgumentException("Seconds must be between 1 and 86400.");
    var record = Option("--record"); using var writer = record is null ? null : new ReplayWriter(record, scenario);
    var count = Math.Min((long)seconds * sim.Rules.TickRate, (long)sim.Rules.RoundSeconds * sim.Rules.TickRate);
    var clock = Stopwatch.StartNew(); var allocated = GC.GetAllocatedBytesForCurrentThread();
    for (long tick = 0; tick < count; tick++)
    {
        for (var i = 0; i < commands.Length; i++) commands[i] = scenario.BotsEnabled ? bot.GetCommand(sim, sim.Robots[i]) : new(i);
        writer?.Write(commands); sim.Step(commands);
    }
    var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated; clock.Stop(); writer?.Complete(sim);
    Console.WriteLine(JsonSerializer.Serialize(new { sim.Tick, sim.Elapsed, wallSeconds = clock.Elapsed.TotalSeconds,
        ticksPerSecond = sim.Tick / clock.Elapsed.TotalSeconds, allocatedBytes = bytes,
        red = new { sim.Alliances[0].Vp, sim.Alliances[0].Rp }, blue = new { sim.Alliances[1].Vp, sim.Alliances[1].Rp },
        result = sim.Result(), hash = sim.StateHash() }, JsonFiles.Options)); return 0;
}
catch (Exception error) when (error is IOException or ArgumentException or JsonException or HttpRequestException or InvalidOperationException)
{ Console.Error.WriteLine(error.Message); return 1; }
