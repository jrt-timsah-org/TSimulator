using TSimulator.Desktop;

try { return new SimulatorApp(args).Run(); }
catch (Exception error)
{
    Console.Error.WriteLine($"TSimulator: {error.Message}\n{error}");
    return 1;
}
