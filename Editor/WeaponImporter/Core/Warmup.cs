#nullable enable annotations

using System.Numerics;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Grip;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;
using WeaponImporter.Core.Weapon;

namespace WeaponImporter.Core;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// Runs the importer's heavy code once on a tiny box weapon, in the background, when the tool
/// opens. The first real import otherwise pays for loading and compiling all of that code, which
/// stalled the editor for about two seconds (measured in the editor gate).
/// </summary>
public static class Warmup
{
    private static int _started;

    /// <summary>Runs once per session; safe to call from any thread.</summary>
    public static void Run()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;
        try
        {
            var asset = BoxPistol();
            var analysis = WeaponAnalyzer.Analyze(asset);
            GripRegions.Of(analysis);
            GripExtractor.ExtractOwn(analysis, "warmup");
            analysis.WeaponBvh.Raycast(Vector3.Zero, Vector3.UnitX, 10f);
        }
        catch (Exception)
        {
            // Warming up is best effort; the real import reports its own errors.
        }
    }

    private static WeaponAsset BoxPistol()
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        var parts = new List<int>();
        var names = new List<string>();
        void Box(string part, Vector3 min, Vector3 max)
        {
            names.Add(part);
            var b = positions.Count;
            for (var i = 0; i < 8; i++)
                positions.Add(new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z));
            int[] faces = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
            foreach (var f in faces)
                indices.Add(b + f);
            for (var t = 0; t < 12; t++)
                parts.Add(names.Count - 1);
        }
        Box("slide", new Vector3(-2f, -0.45f, 0.5f), new Vector3(5.5f, 0.45f, 1.4f));   // slide
        Box("frame", new Vector3(-2f, -0.4f, -0.1f), new Vector3(4f, 0.4f, 0.5f));      // frame
        Box("grip", new Vector3(-2.2f, -0.55f, -3.6f), new Vector3(-0.6f, 0.55f, -0.1f)); // grip
        Box("trigger", new Vector3(0.1f, -0.1f, -0.7f), new Vector3(0.3f, 0.1f, -0.1f));  // trigger
        var skeleton = Skeleton.Create(new[] { new BoneDefinition("root", null, XForm.Identity) });
        var mesh = new TriMesh(positions.ToArray(), indices.ToArray(), new int[positions.Count], parts.ToArray(), names);
        var idle = new Clip("idle", 30f, true);
        idle.Frames.Add(new[] { XForm.Identity });
        idle.Frames.Add(new[] { XForm.Identity });
        return new WeaponAsset { Name = "warmup", Kind = SourceKind.Synthetic, SourcePath = "warmup.fbx", Skeleton = skeleton, Mesh = mesh, Clips = new List<Clip> { idle } };
    }
}
