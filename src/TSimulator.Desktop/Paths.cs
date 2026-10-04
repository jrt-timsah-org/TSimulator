namespace TSimulator.Desktop;
public static class Paths
{
    public static string Content { get; } = FindContent();
    public static string User { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TSimulator");
    public static string Settings => Path.Combine(User, "settings.json");
    public static string Models => Path.Combine(User, "models", "official");
    public static string Replays => Path.Combine(User, "replays");
    public static string? OfficialDirectory => File.Exists(Path.Combine(Models, "field.obj")) ? Models
        : File.Exists(Path.Combine(Content, "assets", "models", "official", "field.obj")) ? Path.Combine(Content, "assets", "models", "official") : null;
    private static string FindContent()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; directory is not null && i < 8; i++, directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "config"))) return directory.FullName;
        if (Directory.Exists(Path.Combine(Environment.CurrentDirectory, "config"))) return Environment.CurrentDirectory;
        return AppContext.BaseDirectory;
    }
}
