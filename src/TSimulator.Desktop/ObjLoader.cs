using System.Globalization;
using System.Numerics;
using Raylib_cs;
namespace TSimulator.Desktop;

/// <summary>Bounded managed OBJ loader; native tinyobj parsing is intentionally avoided.</summary>
public static class ObjLoader
{
    private sealed record Vertex(Vector3 Position, Vector3 Normal, Color Color);
    public static unsafe Model Load(string path)
    {
        if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("OBJ exceeds 32 MiB.");
        var positions=new List<Vector3>();var triangles=new List<Vertex>();
        var palette=new Dictionary<string,Color>();var colour=Color.White;
        foreach(var raw in File.ReadLines(path))
        {
            var tokens=raw.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            if(tokens.Length==0 || tokens[0].StartsWith('#'))continue;
            if(tokens[0]=="mtllib" && tokens.Length>=2)
            {
                var materialPath=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,string.Join(' ',tokens.Skip(1))));
                // Only local material files within the model folder are allowed.
                if(Path.GetDirectoryName(materialPath)==Path.GetDirectoryName(Path.GetFullPath(path)) && File.Exists(materialPath))
                {
                    string? name=null;
                    foreach(var line in File.ReadLines(materialPath))
                    {
                        var parts=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                        if(parts.Length>=2 && parts[0]=="newmtl")name=parts[1];
                        if(parts.Length>=4 && parts[0]=="Kd" && name is not null)
                            palette[name]=new Color((byte)(Math.Clamp(Float(parts[1]),0,1)*255),(byte)(Math.Clamp(Float(parts[2]),0,1)*255),(byte)(Math.Clamp(Float(parts[3]),0,1)*255),(byte)255);
                    }
                }
            }
            else if(tokens[0]=="usemtl" && tokens.Length>=2)colour=palette.GetValueOrDefault(tokens[1],Color.White);
            else if(tokens[0]=="v" && tokens.Length>=4)
            {
                if(positions.Count>=500000)throw new InvalidDataException("OBJ has too many vertices.");
                positions.Add(new(Float(tokens[1]),Float(tokens[2]),Float(tokens[3])));
            }
            else if(tokens[0]=="f" && tokens.Length>=4)
            {
                if(tokens.Length>1000)throw new InvalidDataException("OBJ face exceeds limit.");
                int Index(string token)
                {
                    int index=int.Parse(token.Split('/')[0],CultureInfo.InvariantCulture);
                    index=index>0?index-1:positions.Count+index;
                    if(index<0 || index>=positions.Count)throw new InvalidDataException("OBJ index out of bounds.");
                    return index;
                }
                var a=positions[Index(tokens[1])];
                for(var i=2;i<tokens.Length-1;i++)
                {
                    if(triangles.Count>=1500000)throw new InvalidDataException("OBJ has too many triangles.");
                    var b=positions[Index(tokens[i])];var c=positions[Index(tokens[i+1])];
                    var n=Vector3.Cross(b-a,c-a);if(n.LengthSquared()<1e-12f)continue;n=Vector3.Normalize(n);
                    triangles.Add(new(a,n,colour));triangles.Add(new(b,n,colour));triangles.Add(new(c,n,colour));
                }
            }
        }
        if(triangles.Count==0)throw new InvalidDataException("OBJ contains no valid triangles.");
        var mesh=new Mesh { VertexCount=triangles.Count,TriangleCount=triangles.Count/3 };
        mesh.Vertices=(float*)Raylib.MemAlloc((uint)(triangles.Count*3*sizeof(float)));
        mesh.Normals=(float*)Raylib.MemAlloc((uint)(triangles.Count*3*sizeof(float)));
        mesh.Colors=(byte*)Raylib.MemAlloc((uint)(triangles.Count*4));
        if(mesh.Vertices==null || mesh.Normals==null || mesh.Colors==null)
        {Raylib.UnloadMesh(mesh);throw new OutOfMemoryException("GPU mesh allocation failed.");}
        for(var i=0;i<triangles.Count;i++)
        {
            var v=triangles[i];mesh.Vertices[i*3]=v.Position.X;mesh.Vertices[i*3+1]=v.Position.Y;mesh.Vertices[i*3+2]=v.Position.Z;
            mesh.Normals[i*3]=v.Normal.X;mesh.Normals[i*3+1]=v.Normal.Y;mesh.Normals[i*3+2]=v.Normal.Z;
            mesh.Colors[i*4]=v.Color.R;mesh.Colors[i*4+1]=v.Color.G;mesh.Colors[i*4+2]=v.Color.B;mesh.Colors[i*4+3]=255;
        }
        Raylib.UploadMesh(ref mesh,false);
        return Raylib.LoadModelFromMesh(mesh);
    }
    private static float Float(string value)
    {
        var result=float.Parse(value,CultureInfo.InvariantCulture);
        if(!float.IsFinite(result) || Math.Abs(result)>100000)throw new InvalidDataException("OBJ coordinate outside supported range.");
        return result;
    }
}
