using System.Numerics;
using Raylib_cs;
using TSimulator.Core;
namespace TSimulator.Desktop;

public sealed class WorldRenderer : IDisposable
{
    private readonly Dictionary<string, Model> models = [];
    private readonly Model cube = Raylib.LoadModelFromMesh(Raylib.GenMeshCube(1, 1, 1));
    private readonly Model disc = Raylib.LoadModelFromMesh(Raylib.GenMeshCylinder(.09f, .02f, 24));
    private bool fieldLoaded;
    public bool OfficialLoaded => fieldLoaded;
    private readonly bool allowOfficial;
    public WorldRenderer(bool allowOfficial = true) { this.allowOfficial = allowOfficial; Reload(); }
    public void Reload()
    {
        foreach (var model in models.Values) Raylib.UnloadModel(model);
        models.Clear(); fieldLoaded = false;
        if (!allowOfficial || Paths.OfficialDirectory is not string directory) return;
        foreach (var name in new[] { "field", "container-red", "container-blue", "container-yellow" })
        {
            var path = Path.Combine(directory, name + ".obj");
            if (!File.Exists(path)) continue;
            var model = ObjLoader.Load(path);
            if (model.MeshCount > 0) { models[name] = model; if (name == "field") fieldLoaded = true; }
            else Raylib.UnloadModel(model);
        }
    }
    public void DrawBox(Box box, Color color) => Raylib.DrawModelEx(cube, box.Center, Vector3.UnitY,
        -box.Yaw * 180 / MathF.PI, box.HalfSize * 2, color);
    private void BoxAt(Vector3 center, Vector3 size, float yaw, Color color) => DrawBox(new(center, size / 2, yaw), color);
    public void Draw(Simulation sim, float alpha, int controlled, int mode)
    {
        if (fieldLoaded) Raylib.DrawModel(models["field"], Vector3.Zero, 1, Color.White);
        else DrawFallback(sim);
        foreach (var obstacle in sim.Scenario.ExtraObstacles)
            BoxAt(new(obstacle.X, obstacle.Height / 2, obstacle.Z), new(obstacle.Width, obstacle.Height, obstacle.Depth), obstacle.Yaw, new(172,139,94,255));
        foreach (var robot in sim.Robots)
        {
            if (robot.Id == controlled && mode == 1) continue;
            var position = Vector3.Lerp(robot.PreviousPosition, robot.Position, alpha);
            var yaw = robot.PreviousYaw + Simulation.Wrap(robot.Yaw - robot.PreviousYaw) * alpha;
            var team = robot.Side == Side.Red ? new Color(240,70,91,255) : new Color(69,141,246,255);
            if (!robot.Alive || robot.Stopped) team = new(90, 99, 113, 255);
            if (robot.Spec.ModelPath is string custom && File.Exists(custom))
            {
                if (!models.TryGetValue(custom, out var model)) { model = Path.GetExtension(custom).Equals(".obj",StringComparison.OrdinalIgnoreCase) ? ObjLoader.Load(custom) : Raylib.LoadModel(custom); models[custom] = model; }
                Raylib.DrawModelEx(model, position, Vector3.UnitY, -yaw * 180 / MathF.PI, Vector3.One * robot.Spec.ModelScale, Color.White);
            }
            else DrawRobot(robot, position, yaw, team);
            for (var i = 0; i < 4; i++)
            {
                var panel = sim.Panel(robot, i); panel = panel with { Center = panel.Center + position - robot.Position,
                    Yaw = yaw + i * MathF.PI / 2 };
                DrawBox(panel, new(22,30,39,255));
                var led = robot.Alive ? (sim.Tick < robot.InvulnerableUntil ? new Color(113,248,169,255) : team) : new Color(38,43,49,255);
                DrawBox(panel with { Center = panel.Center + new Vector3(0,.085f,0), HalfSize = new(.012f,.008f,.09f) }, led);
                DrawBox(panel with { Center = panel.Center - new Vector3(0,.085f,0), HalfSize = new(.012f,.008f,.09f) }, led);
            }
            if (robot.Id == controlled)
                Raylib.DrawCircle3D(position + new Vector3(0,.016f,0), .6f, Vector3.UnitX, 90, new(113,248,169,255));
            Raylib.DrawCylinder(position + new Vector3(0,robot.Spec.Height+.02f,0), .055f,.055f,.04f,12, team);
        }
        foreach (var container in sim.Containers)
        {
            var key = "container-" + (container.Side == Side.Red ? "red" : container.Side == Side.Blue ? "blue" : "yellow");
            if (models.TryGetValue(key, out var model)) Raylib.DrawModel(model, container.Position, 1, Color.White);
            else
            {
                var color = container.Side == Side.Red ? new Color(240,70,91,255) : container.Side == Side.Blue ? new Color(69,141,246,255) : new Color(250,197,74,255);
                BoxAt(container.Position, new(.12f,.12f,.12f), 0, color);
                BoxAt(container.Position + new Vector3(.061f,0,0), new(.002f,.085f,.085f), 0, new(180,189,202,255));
            }
        }
        foreach (var p in sim.Projectiles)
        {
            var pos = Vector3.Lerp(p.PreviousPosition, p.Position, alpha);
            Raylib.DrawModel(disc, pos, 1, new(250,224,151,255));
            Raylib.DrawLine3D(pos, pos - p.Velocity * .04f, new(243,183,79,170));
        }
    }
    private void DrawRobot(Robot robot, Vector3 position, float yaw, Color team)
    {
        var spec = robot.Spec;
        var space = new Box(position, Vector3.One, yaw);
        BoxAt(position + new Vector3(0,.16f,0), new(spec.Depth,.22f,spec.Width), yaw, new(36,46,61,255));
        BoxAt(position + new Vector3(0,.285f,0), new(spec.Depth*.9f,.07f,spec.Width*.9f), yaw, team);
        foreach (var x in new[] { -1, 1 }) foreach (var z in new[] { -1, 1 })
        {
            var p = space.World(new(x*spec.Depth*.32f,.12f,z*spec.Width*.47f));
            BoxAt(p,new(.2f,.2f,.07f),yaw,new(17,23,31,255));
            BoxAt(p + new Vector3(0,.012f,0),new(.105f,.105f,.08f),yaw,new(113,124,139,255));
        }
        BoxAt(space.World(new(-.1f,spec.Height*.6f,0)),new(.19f,spec.Height*.5f,.2f),yaw,new(151,162,177,255));
        BoxAt(space.World(new(.11f,spec.ShotHeight+.024f,0)),new(.42f,.08f,.24f),yaw,new(52,67,83,255));
        if(spec.ShotInterval<.15f)
            foreach(var z in new[] { -.09f,.09f })BoxAt(space.World(new(.28f,spec.ShotHeight+.08f,z)),new(.25f,.05f,.06f),yaw,new(106,145,170,255));
        var magazineHeight=.05f+Math.Min(.3f,spec.MagazineCapacity/500f);
        BoxAt(space.World(new(-spec.Depth*.27f,.34f+magazineHeight/2,0)),new(.18f,magazineHeight,.2f),yaw,new(200,163,80,255));
        BoxAt(space.World(new(-.15f,spec.Height*.92f,0)),new(.12f,.08f,.12f),yaw,new(48,58,72,255));
        BoxAt(space.World(new(-.088f,spec.Height*.92f,0)),new(.006f,.04f,.055f),yaw,new(93,197,225,255));
        BoxAt(space.World(new(.15f,robot.ArmHeight,.18f)),new(spec.ArmReach*1.2f,.035f,.035f),yaw,new(164,174,187,255));
        BoxAt(space.World(new(spec.Depth/2+spec.ArmReach,robot.ArmHeight,.04f)),new(.07f,.06f,.16f),yaw,team);
    }
    public void DrawPreview(RobotSpec spec)
    {
        BoxAt(new(0,-.02f,0),new(2.1f,.04f,1.5f),0,new(39,52,70,255));
        DrawRobot(new(-1,Side.Red,spec,Vector3.Zero,0,60),Vector3.Zero,0,new(106,237,183,255));
        Raylib.DrawLine3D(new(-.6f,.005f,0),new(.9f,.005f,0),new(250,101,119,255));
        Raylib.DrawLine3D(new(0,.005f,-.6f),new(0,.005f,.6f),new(103,169,255,255));
    }
    private void DrawFallback(Simulation sim)
    {
        BoxAt(new(6,0,4), new(sim.Rules.FieldWidth,.022f,sim.Rules.FieldDepth),0,new(158,174,190,255));
        for (var x = 0; x <= 12; x++) Raylib.DrawLine3D(new(x,.013f,0),new(x,.013f,8),new(134,152,171,255));
        for (var z = 0; z <= 8; z++) Raylib.DrawLine3D(new(0,.013f,z),new(12,.013f,z),new(134,152,171,255));
        foreach (var side in new[] { Side.Red, Side.Blue })
        {
            var c = side == Side.Red ? new Color(213,52,78,255) : new Color(47,101,219,255);
            foreach (var (x,z,w,d) in new[] { (1.2f,.6f,2.4f,1.2f),(.6f,2.4f,1.2f,2.4f) })
                BoxAt(new(side==Side.Red?x:12-x,.013f,side==Side.Red?z:8-z),new(w,.002f,d),0,c);
            BoxAt(new(side==Side.Red?.75f:11.25f,.013f,side==Side.Red?6.8f:1.2f),new(1.5f,.002f,2.4f),0,new(95,119,162,255));
            var spotX = side == Side.Red ? 1.825f : 10.175f; var spotZ = side == Side.Red ? 7.35f : .65f;
            foreach (var z in new[] { -.45f,-.15f,.15f,.45f }) BoxAt(new(spotX,.55f,spotZ+z),new(.03f,1.07f,.03f),0,new(74,90,111,255));
            foreach (var y in new[] { .3f,.625f,.85f,1.085f }) BoxAt(new(spotX,y,spotZ),new(.25f,.025f,.9f),0,new(103,124,157,255));
            BoxAt(new(side==Side.Red?1.57f:10.43f,.43f,side==Side.Red?6.8f:1.2f),new(.03f,.84f,2.4f),0,new(32,44,61,255));
            for (var i=0;i<5;i++)
            {
                // Station geometry is static even after containers are taken.
                var z = 5.69f + i*.24f - (i>=3?.04f:0); var h=i<3?.3f:.45f;
                BoxAt(new(side==Side.Red?1.86f:10.14f,h/2,side==Side.Red?z:8-z),new(.18f,h,.22f),0,new(164,174,190,255));
            }
        }
        BoxAt(new(6,.225f,4),new(2.4f,.45f,.21f),-MathF.PI/4,new(164,174,190,255));
        BoxAt(new(6,.43f,4),new(1.5f,.84f,.04f),-MathF.PI/4,new(32,44,61,255));
    }
    public void Dispose()
    {
        foreach (var model in models.Values) Raylib.UnloadModel(model);
        Raylib.UnloadModel(cube); Raylib.UnloadModel(disc);
    }
}
