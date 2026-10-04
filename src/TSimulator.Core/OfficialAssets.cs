using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace TSimulator.Core;

public static class OfficialAssets
{
    public const string Source = "https://core.scramble-robot.org/rule/core-2-rulenavi/";
    public static async Task FetchAsync(string directory, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TSimulator/0.1 (+https://github.com/jrt-timsah-org/TSimulator)");
        var html = await client.GetStringAsync(Source, cancellationToken);
        Import(html, directory);
    }
    public static JsonDocument Extract(string html, string name)
    {
        var marker = "window." + name;
        var offset = html.IndexOf(marker, StringComparison.Ordinal);
        if (offset < 0) throw new InvalidDataException($"Official page has no {name} data.");
        offset = html.IndexOf('=', offset) + 1;
        var bytes = Encoding.UTF8.GetBytes(html[offset..].TrimStart());
        var reader = new Utf8JsonReader(bytes);
        if (!JsonDocument.TryParseValue(ref reader, out var document)) throw new InvalidDataException("Incomplete model data.");
        return document;
    }
    public static void Import(string html, string directory)
    {
        using var rules = Extract(html, "RB");
        var version = rules.RootElement.GetProperty("meta").GetProperty("ver").GetString();
        if (version != "V27.2.0") throw new InvalidDataException($"Source changed to {version}; review the new rules/model before updating the importer.");
        using var data = Extract(html, "F3D"); var root = data.RootElement;
        Directory.CreateDirectory(directory);
        var palette = new Dictionary<string, string>
        {
            ["floor"]="0.65 0.70 0.76", ["line_red"]="0.85 0.10 0.18", ["line_blue"]="0.10 0.30 0.85",
            ["line_light"]="0.55 0.65 0.85", ["alu"]="0.70 0.75 0.81", ["board"]="0.78 0.63 0.42",
            ["panel"]="0.10 0.13 0.18", ["shelf"]="0.57 0.64 0.78", ["plate"]="0.85 0.88 0.92",
            ["acc"]="1 1 1", ["body"]="0.65 0.70 0.78", ["seal"]="0.9 0.92 0.96"
        };
        var mtl = string.Join('\n', palette.Select(x => $"newmtl {x.Key}\nKd {x.Value}\nKa 0.2 0.2 0.2\nKs 0.1 0.1 0.1\nNs 24\n"));
        File.WriteAllText(Path.Combine(directory, "official.mtl"), mtl);
        using (var output = new StreamWriter(Path.Combine(directory, "field.obj")))
        {
            output.WriteLine("# CoRE-2 rule navigation CAD-derived geometry. See source.json.\nmtllib official.mtl");
            var index = 1; var scale = root.GetProperty("scale").GetSingle() * 1000;
            foreach (var group in root.GetProperty("groups").EnumerateArray())
                WriteGroup(output, group, group.GetProperty("m").GetString()!, scale, 0, ref index);
        }
        var container = root.GetProperty("cont");
        foreach (var (name, colour) in new[] { ("red", "0.85 0.1 0.18"), ("blue", "0.1 0.3 0.85"), ("yellow", "1 0.72 0.08") })
        {
            File.WriteAllText(Path.Combine(directory, $"container-{name}.mtl"), mtl.Replace("newmtl acc\nKd 1 1 1", $"newmtl acc\nKd {colour}"));
            using var output = new StreamWriter(Path.Combine(directory, $"container-{name}.obj"));
            output.WriteLine($"mtllib container-{name}.mtl"); var index = 1;
            foreach (var group in container.GetProperty("geo").EnumerateObject())
                WriteGroup(output, group.Value, group.Name, container.GetProperty("q").GetSingle() * 1000,
                    container.GetProperty("off").GetSingle() / 1000, ref index);
        }
        JsonFiles.Save(Path.Combine(directory, "source.json"), new { source = Source, version,
            fetchedUtc = DateTimeOffset.UtcNow, sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html))),
            redistribution = "Upstream rights retained. Bundled models are included with user-provided permission to freely use them." });
    }
    private static void WriteGroup(StreamWriter output, JsonElement group, string material, float scale, float offset, ref int index)
    {
        var positions = Convert.FromBase64String(group.GetProperty("p").GetString()!);
        var indices = Convert.FromBase64String(group.GetProperty("i").GetString()!);
        if (positions.Length % 6 != 0 || indices.Length % 6 != 0 || scale <= 0) throw new InvalidDataException("Invalid mesh buffers.");
        output.WriteLine($"g {material}_{index}\nusemtl {material}");
        for (var i = 0; i < positions.Length; i += 6)
        {
            var x = BinaryPrimitives.ReadUInt16LittleEndian(positions.AsSpan(i)) / scale - offset;
            var y = BinaryPrimitives.ReadUInt16LittleEndian(positions.AsSpan(i + 2)) / scale - offset;
            var z = BinaryPrimitives.ReadUInt16LittleEndian(positions.AsSpan(i + 4)) / scale - offset;
            output.WriteLine(FormattableString.Invariant($"v {x:R} {y:R} {z:R}"));
        }
        for (var i = 0; i < indices.Length; i += 6)
        {
            var a = BinaryPrimitives.ReadUInt16LittleEndian(indices.AsSpan(i));
            var b = BinaryPrimitives.ReadUInt16LittleEndian(indices.AsSpan(i + 2));
            var c = BinaryPrimitives.ReadUInt16LittleEndian(indices.AsSpan(i + 4));
            if (a >= positions.Length / 6 || b >= positions.Length / 6 || c >= positions.Length / 6)
                throw new InvalidDataException("Mesh index out of bounds.");
            output.WriteLine($"f {a + index} {b + index} {c + index}");
            output.WriteLine($"f {c + index} {b + index} {a + index}"); // Upstream viewer uses double-sided materials.
        }
        index += positions.Length / 6;
    }
}
