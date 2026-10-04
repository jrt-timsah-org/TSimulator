using System.Text.Json;
namespace TSimulator.Core;

public sealed record ReplayHeader(int Format, Scenario Scenario);
public sealed record ReplayFrame(RobotCommand[] Commands);
public sealed record ReplayFooter(long Ticks, string StateHash);
public sealed class ReplayWriter : IDisposable
{
    private readonly StreamWriter writer;
    private bool completed;
    public ReplayWriter(string path, Scenario scenario)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        writer = new(path);
        writer.WriteLine(JsonSerializer.Serialize(new ReplayHeader(2, scenario)));
    }
    public void Write(ReadOnlySpan<RobotCommand> commands) => writer.WriteLine(JsonSerializer.Serialize(new ReplayFrame(commands.ToArray())));
    public void Complete(Simulation sim)
    {
        if (completed) return;
        writer.WriteLine(JsonSerializer.Serialize(new ReplayFooter(sim.Tick, sim.StateHash()))); writer.Flush(); completed = true;
    }
    public void Dispose() => writer.Dispose();
}
public static class Replay
{
    public static Simulation Play(string path)
    {
        using var reader = new StreamReader(path);
        var header = JsonSerializer.Deserialize<ReplayHeader>(reader.ReadLine() ?? "") ?? throw new InvalidDataException("Missing replay header.");
        if (header.Format is not (1 or 2)) throw new InvalidDataException("Unsupported replay format.");
        var sim = new Simulation(header.Scenario, legacyReplay:header.Format == 1);
        string? line; bool verified = false;
        while ((line = reader.ReadLine()) is not null)
        {
            using var json = JsonDocument.Parse(line);
            if (json.RootElement.TryGetProperty("StateHash", out _))
            {
                var footer = JsonSerializer.Deserialize<ReplayFooter>(line)!;
                if (footer.Ticks != sim.Tick || footer.StateHash != sim.StateHash())
                    throw new InvalidDataException($"Replay diverged at tick {sim.Tick}. Cross-architecture floating-point identity is not guaranteed.");
                verified = true;
                if (reader.ReadLine() is not null) throw new InvalidDataException("Data after replay footer.");
                break;
            }
            var frame = JsonSerializer.Deserialize<ReplayFrame>(line) ?? throw new InvalidDataException("Invalid replay frame.");
            if (sim.Finished) throw new InvalidDataException("Replay contains commands after round end.");
            sim.Step(frame.Commands);
        }
        if (!verified) throw new InvalidDataException("Replay incomplete: missing verification footer.");
        return sim;
    }
}
