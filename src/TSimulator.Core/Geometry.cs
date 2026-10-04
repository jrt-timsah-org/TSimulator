using System.Numerics;
namespace TSimulator.Core;

public readonly record struct Box(Vector3 Center, Vector3 HalfSize, float Yaw = 0)
{
    public Vector3 Local(Vector3 point)
    {
        var d = point - Center; var c = MathF.Cos(Yaw); var s = MathF.Sin(Yaw);
        return new(c * d.X + s * d.Z, d.Y, -s * d.X + c * d.Z);
    }
    public Vector3 World(Vector3 local)
    {
        var c = MathF.Cos(Yaw); var s = MathF.Sin(Yaw);
        return Center + new Vector3(c * local.X - s * local.Z, local.Y, s * local.X + c * local.Z);
    }
    public bool Intersects(Box other)
    {
        if (Math.Abs(Center.Y - other.Center.Y) >= HalfSize.Y + other.HalfSize.Y) return false;
        Span<Vector2> axes = stackalloc Vector2[4];
        axes[0] = new(MathF.Cos(Yaw), MathF.Sin(Yaw)); axes[1] = new(-axes[0].Y, axes[0].X);
        axes[2] = new(MathF.Cos(other.Yaw), MathF.Sin(other.Yaw)); axes[3] = new(-axes[2].Y, axes[2].X);
        var delta = new Vector2(other.Center.X - Center.X, other.Center.Z - Center.Z);
        foreach (var axis in axes)
        {
            var r1 = HalfSize.X * Math.Abs(Vector2.Dot(axis, axes[0])) + HalfSize.Z * Math.Abs(Vector2.Dot(axis, axes[1]));
            var r2 = other.HalfSize.X * Math.Abs(Vector2.Dot(axis, axes[2])) + other.HalfSize.Z * Math.Abs(Vector2.Dot(axis, axes[3]));
            if (Math.Abs(Vector2.Dot(delta, axis)) >= r1 + r2 - .00001f) return false;
        }
        return true;
    }
    public bool Sweep(Vector3 start, Vector3 end, Vector3 expansion, out float fraction)
    {
        var a = Local(start); var b = Local(end); var d = b - a; var h = HalfSize + expansion;
        float lo = 0, hi = 1;
        for (var i = 0; i < 3; i++)
        {
            float p = i == 0 ? a.X : i == 1 ? a.Y : a.Z;
            float v = i == 0 ? d.X : i == 1 ? d.Y : d.Z;
            float bound = i == 0 ? h.X : i == 1 ? h.Y : h.Z;
            if (Math.Abs(v) < 1e-8f) { if (p < -bound || p > bound) { fraction = 0; return false; } }
            else
            {
                float t1 = (-bound - p) / v, t2 = (bound - p) / v;
                if (t1 > t2) (t1, t2) = (t2, t1);
                lo = Math.Max(lo, t1); hi = Math.Min(hi, t2);
                if (lo > hi) { fraction = 0; return false; }
            }
        }
        fraction = lo; return true;
    }
    public Vector2[] Corners()
    {
        Vector2[] corners = new Vector2[4];
        for (var i = 0; i < 4; i++)
        {
            var v = World(new((i & 1) == 0 ? -HalfSize.X : HalfSize.X, 0, (i & 2) == 0 ? -HalfSize.Z : HalfSize.Z));
            corners[i] = new(v.X, v.Z);
        }
        return corners;
    }
}
public static class Arena
{
    public static Box[] Obstacles(Scenario scenario)
    {
        List<Box> boxes = [
            new(new(1.725f, .43f, 6.8f), new(.225f, .42f, 1.2f)),
            new(new(10.275f, .43f, 1.2f), new(.225f, .42f, 1.2f)),
            new(new(6, .23f, 4), new(1.2f, .23f, .225f), -MathF.PI / 4),
            new(new(6, .445f, 4), new(.78f, .395f, .03f), -MathF.PI / 4),
            new(new(1.82f, .55f, 7.35f), new(.13f, .535f, .45f)),
            new(new(10.18f, .55f, .65f), new(.13f, .535f, .45f))
        ];
        boxes.AddRange(scenario.ExtraObstacles.Select(x => new Box(new(x.X, x.Height / 2, x.Z), new(x.Width / 2, x.Height / 2, x.Depth / 2), x.Yaw)));
        return [.. boxes];
    }
    public static bool InPitArea(Side side, Vector2 p) => side == Side.Red
        ? p.X is >= 0 and <= 1.5f && p.Y is >= 5.6f and <= 8
        : p.X is >= 10.5f and <= 12 && p.Y is >= 0 and <= 2.4f;
    public static bool InPitZone(Side side, Box body) => body.Corners().All(p => InPitArea(side, p)
        && (side == Side.Red ? p.Y >= 6.5f : p.Y <= 1.5f));
    public static Vector3 SpotPosition(Side side, int cell)
    {
        float y = (cell / 3) switch { 0 => .3f, 1 => .625f, _ => .85f };
        return new(side == Side.Red ? 1.825f : 10.175f, y + .06f,
            side == Side.Red ? 7.05f + cell % 3 * .3f : .95f - cell % 3 * .3f);
    }
    public static readonly int[][] Lines = [[0,1,2],[3,4,5],[6,7,8],[0,3,6],[1,4,7],[2,5,8],[0,4,8],[2,4,6]];
}
