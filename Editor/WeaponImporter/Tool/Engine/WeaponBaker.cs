using System.Text.Json;
using System.Text.Json.Nodes;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Generation;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Output;
using WeaponImporter.Core.Setup;
using WeaponImporter.Core.Weapon;
using N = System.Numerics;

namespace WeaponImporter.Tool;

/// <summary>What a bake produced.</summary>
public sealed class BakeResult
{
    public bool Success { get; set; }
    public string ModelPath { get; set; } = "";
    public string PrefabPath { get; set; } = "";
    public string ProfilePath { get; set; } = "";
    public string CorrectedModelPath { get; set; } = "";
    /// <summary>The first-person viewmodel (empty when the file has no first-person arms).</summary>
    public string FirstPersonModelPath { get; set; } = "";
    /// <summary>The weapon's AnimGraph (empty when there is no idle animation to build it on).</summary>
    public string GraphPath { get; set; } = "";
    public string SetupPath { get; set; } = "";
    public List<string> Files { get; } = new();
    public Dictionary<string, IReadOnlyList<string>> Errors { get; } = new();
    public List<string> Sequences { get; } = new();
    public List<string> Notes { get; } = new();
}

/// <summary>
/// Turns a session into game-ready assets: model DMX + clip DMX, materials, the VMDL with
/// attachments and events, and a prefab carrying the baked <c>WeaponHold</c>. Everything is
/// written under the output folder, registered and compiled; compile errors come back per file.
/// </summary>
public static partial class WeaponBaker
{
    public static async Task<BakeResult> BakeAsync( ImportSession session, IProgress<string> progress = null, CancellationToken cancel = default )
    {
        await EditorThread.SwitchToMainThread();
        var result = new BakeResult();
        var setup = session.Setup;
        var analysis = session.Analysis;
        if ( setup is null || analysis is null )
        {
            result.Notes.Add( "Nothing to bake." );
            return result;
        }
        if ( session.Grip is null )
            await session.ResolveGripAsync( progress, cancel );

        var folder = session.OutputFolder;
        var name = setup.Name;
        // Never write over the file being imported (a VMDL already at weapons/<name>/<name>.vmdl).
        var source = SourceAbsolute( session.SourcePath );
        AssetCompiler.Protected = source;
        if ( source is not null && SamePath( AssetCompiler.Absolute( $"{folder}/{name}.vmdl" ), source ) )
        {
            name = $"{name}_weapon";
            result.Notes.Add( $"The source is {folder}/{setup.Name}.vmdl, so the baked weapon is written as {name} beside it (the source is left untouched)." );
        }
        var generated = $"{folder}/generated";
        var modelPath = $"{folder}/{name}.vmdl";
        var prefabPath = $"{folder}/{name}.prefab";
        result.ModelPath = modelPath;
        result.PrefabPath = prefabPath;
        result.SetupPath = session.SetupPath;

        // 1. Geometry, clips and materials (see WeaponBaker.Generate.cs).
        progress?.Report( "Writing model data" );
        var assetsRoot = AssetCompiler.AssetsRoot;
        ExportedWeapon exported;
        try
        {
            exported = await Task.Run( () => Generate( session, folder, generated, name, assetsRoot ), cancel );
        }
        catch ( Exception e ) when ( e is not OperationCanceledException )
        {
            await EditorThread.SwitchToMainThread();
            result.Errors["export"] = new[] { e.Message };
            result.Notes.Add( e.ToString() );
            session.Revalidate( result.Errors );
            session.MarkChanged();
            return result;
        }
        await EditorThread.SwitchToMainThread();
        result.Files.AddRange( exported.WrittenFiles );
        foreach ( var (rel, text) in exported.TextFiles )
        {
            AssetCompiler.WriteText( rel, text );
            result.Files.Add( rel );
        }
        foreach ( var (rel, bytes) in exported.BinaryFiles )
        {
            AssetCompiler.WriteBytes( rel, bytes );
            result.Files.Add( rel );
        }
        result.Notes.AddRange( exported.Notes );

        // 2. VMDL.
        progress?.Report( "Writing model" );
        var vmdl = new VmdlBuilder { MeshFile = exported.MeshFile };
        vmdl.MaterialRemaps.AddRange( exported.MaterialRemaps );
        var sequenceByRole = new Dictionary<AnimationRole, string>();
        foreach ( var clip in exported.Clips )
        {
            var roles = setup.WeaponAnimations.Where( kv => kv.Value.Clip == clip.Clip ).Select( kv => kv.Key ).ToList();
            foreach ( var r in roles )
                sequenceByRole[r] = clip.Sequence;
            // A variant carries its role's events too (every slash hits, every shot fires).
            var eventRoles = roles.Concat( RolesOfVariant( setup, clip.Clip ) ).Distinct().ToList();
            var events = setup.Events.Where( e => eventRoles.Contains( e.Role ) ).Select( e => (WeaponEvent.EngineName( e.Kind ), e.Time) ).ToList();
            vmdl.Animations.Add( new VmdlAnimation
            {
                Name = clip.Sequence,
                File = clip.File,
                Looping = roles.Any( AnimationRoles.Loops ),
                FrameCount = clip.FrameCount,
                Fps = clip.Fps,
                Events = events,
            } );
            result.Sequences.Add( clip.Sequence );
        }
        foreach ( var part in setup.Parts.Where( p => p.Bone.Length > 0 ) )
            vmdl.KeepBones.Add( EngineNames.Bone( part.Bone ) );
        // AnimGraph for the weapon's own sequences (first-person code drives it with the usual parameters).
        if ( exported.Clips.Count > 0 && sequenceByRole.ContainsKey( AnimationRole.Idle ) )
        {
            var graphClips = sequenceByRole.Select( kv => new Core.Graph.GraphClip( kv.Key, kv.Value, AnimationRoles.Loops( kv.Key ), ActionTiming.Seconds( setup, session.Analysis?.Asset, kv.Key ) ) { Variants = VariantSequences( setup, kv.Key ) } ).ToList();
            var graphPath = $"{folder}/{name}.vanmgrph";
            WriteEditable( result, setup, graphPath, Core.Graph.WeaponGraphGenerator.Generate( name, graphClips, null, out _, modelPath ) );
            result.Files.Add( graphPath );
            result.GraphPath = graphPath;
            vmdl.AnimGraph = graphPath;
        }
        AddAttachments( vmdl, session, exported.Rig );
        AssetCompiler.WriteText( modelPath, vmdl.Build() );
        result.Files.Add( modelPath );

        // 2b. First-person viewmodel: the file's own arms, weapon and camera, as authored.
        if ( exported.FirstPerson is { } fp )
        {
            var fpName = $"{name}_fp";
            var fpModelPath = $"{folder}/{fpName}.vmdl";
            var fpVmdl = new VmdlBuilder { MeshFile = exported.FirstPersonMeshFile };
            fpVmdl.MaterialRemaps.AddRange( exported.FirstPersonMaterialRemaps );
            var fpSequences = new Dictionary<AnimationRole, string>();
            foreach ( var clip in exported.FirstPersonClips )
            {
                var roles = setup.WeaponAnimations.Where( kv => kv.Value.Clip == clip.Clip ).Select( kv => kv.Key ).ToList();
                foreach ( var r in roles )
                    fpSequences[r] = clip.Sequence;
                var eventRoles = roles.Concat( RolesOfVariant( setup, clip.Clip ) ).Distinct().ToList();
                var events = setup.Events.Where( e => eventRoles.Contains( e.Role ) ).Select( e => (WeaponEvent.EngineName( e.Kind ), e.Time) ).ToList();
                fpVmdl.Animations.Add( new VmdlAnimation { Name = clip.Sequence, File = clip.File, Looping = roles.Any( AnimationRoles.Loops ), FrameCount = clip.FrameCount, Fps = clip.Fps, Events = events } );
            }
            foreach ( var (point, p) in new[] { ("muzzle", setup.Muzzle), ("eject", setup.Eject) } )
                if ( p is not null && fp.Asset.Skeleton.IndexOf( p.Bone ) >= 0 )
                    fpVmdl.Attachments.Add( new VmdlAttachment( point, EngineNames.Bone( p.Bone ), V.Of( p.Position ) * setup.Scale, V.Q( p.Rotation ) ) );
            if ( fpSequences.ContainsKey( AnimationRole.Idle ) )
            {
                var fpClips = fpSequences.Select( kv => new Core.Graph.GraphClip( kv.Key, kv.Value, AnimationRoles.Loops( kv.Key ), ActionTiming.Seconds( setup, session.Analysis?.Asset, kv.Key ) ) { Variants = VariantSequences( setup, kv.Key ) } ).ToList();
                var fpGraph = $"{folder}/{fpName}.vanmgrph";
                var camera = fp.CameraBone.Length > 0 ? EngineNames.Bone( fp.CameraBone ) : null;
                WriteEditable( result, setup, fpGraph, Core.Graph.WeaponGraphGenerator.Generate( fpName, fpClips, camera, out _, fpModelPath ) );
                result.Files.Add( fpGraph );
                fpVmdl.AnimGraph = fpGraph;
            }
            AssetCompiler.WriteText( fpModelPath, fpVmdl.Build() );
            result.Files.Add( fpModelPath );
            result.FirstPersonModelPath = fpModelPath;
            result.Notes.Add( $"First-person viewmodel: {fpName}, seen from {fp.EyeSource} (fine-tune with Offset on Weapon Viewmodel)." );
        }

        // 3a. Corrected third-person animations (new clips; the character's own stay untouched).
        if ( setup.BakeCorrectedAnimations && session.Analysis is not null )
        {
            try
            {
                var corrected = await CorrectedAnimationBaker.BakeAsync( session, folder, name, progress, cancel );
                result.Files.AddRange( corrected.Files );
                result.Notes.AddRange( corrected.Errors );
                if ( corrected.Clips.Count > 0 )
                {
                    setup.CorrectedModel = corrected.ModelPath;
                    setup.CorrectedClips = corrected.Clips.ToDictionary( c => c.Role, c => c.Sequence );
                    result.CorrectedModelPath = corrected.ModelPath;
                }
            }
            catch ( Exception e ) when ( e is not OperationCanceledException )
            {
                result.Errors["corrected animations"] = new[] { e.Message };
            }
        }

        // 3. Grip profile (reusable) and the prefab holding the weapon with it.
        progress?.Report( "Writing grip profile and prefab" );
        var profilePath = $"{folder}/{name}.wgrip";
        AssetCompiler.WriteText( profilePath, BuildProfile( session, sequenceByRole ) );
        result.Files.Add( profilePath );
        result.ProfilePath = profilePath;
        WriteEditable( result, setup, prefabPath, BuildPrefab( session, modelPath, prefabPath, sequenceByRole, profilePath, result.FirstPersonModelPath, exported.FirstPerson ) );
        result.Files.Add( prefabPath );

        // 4. Setup (so reimports and templates keep every choice).
        session.Save();
        result.Files.Add( session.SetupPath );

        // 5. Register + compile.
        progress?.Report( "Compiling" );
        await AssetCompiler.RegisterAsync( result.Files );
        await EditorThread.Delay( 1500, cancel );
        // The model references the graph, so the graph compiles first.
        if ( result.GraphPath.Length > 0 )
        {
            var g = await AssetCompiler.CompileAsync( result.GraphPath, 60f, cancel );
            if ( !g.Success )
                result.Errors[result.GraphPath] = g.Errors;
        }
        var compile = await AssetCompiler.CompileAsync( modelPath, 240f, cancel );
        if ( !compile.Success )
            result.Errors[modelPath] = compile.Errors;
        foreach ( var vmat in result.Files.Where( f => f.EndsWith( ".vmat" ) ) )
        {
            var c = await AssetCompiler.CompileAsync( vmat, 60f, cancel );
            if ( !c.Success )
                result.Errors[vmat] = c.Errors;
        }
        if ( result.CorrectedModelPath.Length > 0 )
        {
            var c = await AssetCompiler.CompileAsync( result.CorrectedModelPath, 240f, cancel );
            if ( !c.Success )
                result.Errors[result.CorrectedModelPath] = c.Errors;
            else if ( await AssetCompiler.LoadModelAsync( result.CorrectedModelPath, setup.CorrectedClips.Values.ToList() ) is not { IsError: false } corrected )
                result.Errors[result.CorrectedModelPath] = new[] { "the corrected animations could not be loaded" };
            else
                result.Notes.Add( $"Corrected animations: {string.Join( ", ", corrected.AnimationNames.Where( setup.CorrectedClips.Values.Contains ) )}" );
        }

        if ( result.FirstPersonModelPath.Length > 0 )
        {
            var fpGraph = result.FirstPersonModelPath[..^".vmdl".Length] + ".vanmgrph";
            if ( result.Files.Contains( fpGraph ) )
            {
                var g = await AssetCompiler.CompileAsync( fpGraph, 60f, cancel );
                if ( !g.Success )
                    result.Errors[fpGraph] = g.Errors;
            }
            var fpCompile = await AssetCompiler.CompileAsync( result.FirstPersonModelPath, 240f, cancel );
            var fpSequences = exported.FirstPersonClips.Select( c => c.Sequence ).Distinct().ToList();
            if ( !fpCompile.Success )
                result.Errors[result.FirstPersonModelPath] = fpCompile.Errors;
            else if ( await AssetCompiler.LoadModelAsync( result.FirstPersonModelPath, fpSequences ) is not { IsError: false } fpModel || !fpSequences.All( fpModel.AnimationNames.Contains ) )
                result.Errors[result.FirstPersonModelPath] = new[] { "the compiled first-person model is missing animations" };
        }

        IReadOnlyCollection<string> compiledSequences = null;
        if ( compile.Success )
        {
            var model = await AssetCompiler.LoadModelAsync( modelPath, result.Sequences );
            if ( model is null || model.IsError )
                result.Errors[modelPath] = new[] { "compiled model could not be loaded" };
            else
                compiledSequences = model.AnimationNames.ToList();
        }
        session.Revalidate( result.Errors, compiledSequences );
        session.MarkChanged();
        result.Success = result.Errors.Count == 0;
        progress?.Report( result.Success ? "Baked" : "Bake finished with errors" );
        return result;
    }

    /// <summary>
    /// Writes a file users may open and edit (animgraphs, the prefab). When the copy on disk no
    /// longer matches what the last bake wrote, it was edited by hand: it is kept and noted
    /// (delete it to have it generated again).
    /// </summary>
    private static void WriteEditable( BakeResult result, WeaponSetup setup, string relative, string text )
    {
        setup.GeneratedHashes ??= new Dictionary<string, string>();
        var abs = AssetCompiler.Absolute( relative );
        if ( System.IO.File.Exists( abs ) && setup.GeneratedHashes.TryGetValue( relative, out var written ) && Fingerprint( System.IO.File.ReadAllText( abs ) ) != written )
        {
            result.Notes.Add( $"Kept your edited {relative} (delete it to have it generated again)." );
            return;
        }
        AssetCompiler.WriteText( relative, text );
        setup.GeneratedHashes[relative] = Fingerprint( text );
    }

    private static string Fingerprint( string text )
        => Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( text.Replace( "\r\n", "\n" ) ) ), 0, 12 );

    /// <summary>Absolute path of the imported file (source of a compiled model), or null.</summary>
    private static string SourceAbsolute( string path )
    {
        if ( string.IsNullOrEmpty( path ) )
            return null;
        var abs = System.IO.Path.IsPathRooted( path ) ? System.IO.Path.GetFullPath( path ) : AssetCompiler.Absolute( path );
        return abs.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ? abs[..^2] : abs;
    }

    private static bool SamePath( string a, string b ) => string.Equals( System.IO.Path.GetFullPath( a ), System.IO.Path.GetFullPath( b ), StringComparison.OrdinalIgnoreCase );

    private static void AddAttachments( VmdlBuilder vmdl, ImportSession session, ExportRig rig )
    {
        var setup = session.Setup;
        var source = session.Analysis.Asset.Skeleton;
        var export = rig.Asset.Skeleton;
        var root = export.IndexOf( rig.RootName );
        var rootWorld = export.RestWorld[root];

        // Attach to the named bone when it survived the export, else re-express on the root.
        void Place( string name, string bone, XForm local )
        {
            var ei = export.IndexOf( bone );
            if ( ei >= 0 )
            {
                vmdl.Attachments.Add( new VmdlAttachment( name, EngineNames.Bone( bone ), local.Pos, local.Rot ) );
                return;
            }
            var si = source.IndexOf( bone );
            var world = si >= 0 ? XForm.Compose( source.RestWorld[si], local ) : local;
            var onRoot = XForm.ToLocal( rootWorld, world );
            vmdl.Attachments.Add( new VmdlAttachment( name, EngineNames.Bone( rig.RootName ), onRoot.Pos, onRoot.Rot ) );
        }
        void Point( string name, PointSetup p )
        {
            if ( p is null )
                return;
            // Stored in source units; generated models are written in canonical inches.
            Place( name, p.Bone, new XForm( V.Of( p.Position ) * setup.Scale, V.Q( p.Rotation ) ) );
        }
        Point( "muzzle", setup.Muzzle );
        Point( "eject", setup.Eject );

        // Hand anchors in weapon space, for games that place hands themselves.
        if ( session.Grip is { } g )
        {
            var r = XForm.ToLocal( rootWorld, g.Right.Wrist );
            vmdl.Attachments.Add( new VmdlAttachment( "hand_r", EngineNames.Bone( rig.RootName ), r.Pos, r.Rot ) );
            if ( g.Left is not null )
            {
                var l = XForm.ToLocal( rootWorld, g.Left.Wrist );
                vmdl.Attachments.Add( new VmdlAttachment( "hand_l", EngineNames.Bone( rig.RootName ), l.Pos, l.Rot ) );
            }
        }
    }

    /// <summary>
    /// The grip as a <c>WeaponGripProfile</c> resource (JSON of its properties): the same values
    /// the prefab's Weapon Hold carries, reusable on other holds and characters.
    /// </summary>
    public static string BuildProfile( ImportSession session, Dictionary<AnimationRole, string> sequenceByRole = null )
    {
        var v = HoldValues( session, sequenceByRole );
        var setup = session.Setup;
        string S( string key ) => v[key]?.GetValue<string>() ?? "";
        var trigger = session.Analysis?.Part( PartKind.Trigger )?.Center ?? N.Vector3.Zero;
        var firstPerson = new JsonObject();
        foreach ( var (role, seq) in sequenceByRole ?? SequencesByRole( setup ) )
            firstPerson[role.ToString().ToLowerInvariant()] = seq;
        string V3( N.Vector3 p ) => $"{p.X.ToString( "0.####", System.Globalization.CultureInfo.InvariantCulture )},{p.Y.ToString( "0.####", System.Globalization.CultureInfo.InvariantCulture )},{p.Z.ToString( "0.####", System.Globalization.CultureInfo.InvariantCulture )}";
        var profile = new JsonObject
        {
            ["PrimaryHand"] = "R",
            ["PrimaryGripTransform"] = S( "RightHand" ),
            ["PrimaryFingerPose"] = S( "RightFingers" ),
            ["SecondaryGripTransform"] = S( "LeftHand" ),
            ["SecondaryFingerPose"] = S( "LeftFingers" ),
            ["TriggerTarget"] = V3( trigger ),
            ["PrimaryElbowHint"] = S( "RightElbowHint" ),
            ["SecondaryElbowHint"] = S( "LeftElbowHint" ),
            ["TwoHanded"] = setup.UseSupportHand,
            ["WeaponOffset"] = S( "WeaponInHold" ),
            ["HoldBone"] = S( "HoldBone" ),
            ["Contacts"] = S( "Contacts" ),
            ["Actions"] = S( "Actions" ),
            ["Smoothing"] = setup.Ik.Smoothing,
            ["ReachSlack"] = setup.Ik.ReachSlack,
            ["TransitionSeconds"] = 0.15f,
            ["HoldType"] = setup.EffectiveHoldType,
            ["Character"] = session.CharacterModel,
            ["CharacterActions"] = S( "CharacterActions" ),
            ["FirstPersonSequences"] = firstPerson.ToJsonString(),
            ["FirstPersonFieldOfView"] = 70f,
        };
        return profile.ToJsonString( new JsonSerializerOptions { WriteIndented = true } );
    }

    private static string BuildPrefab( ImportSession session, string modelPath, string prefabPath, Dictionary<AnimationRole, string> sequenceByRole, string profilePath, string firstPersonModel = "", FirstPersonRig firstPerson = null )
    {
        var setup = session.Setup;
        var values = HoldValues( session, sequenceByRole ).ToDictionary( kv => kv.Key, kv => kv.Value );
        values["Profile"] = profilePath;
        var hold = new PrefabComponent( "WeaponImporter.WeaponHold", values );
        var root = new PrefabObject { Name = setup.Name, Tags = "weapon" };
        root.Components.Add( PrefabBuilder.ModelRenderer( modelPath ) );
        root.Components.Add( hold );
        if ( !string.IsNullOrEmpty( firstPersonModel ) && firstPerson is not null )
        {
            // The first-person view: shown only to the player holding the weapon, in sync with the hold.
            string Q( System.Numerics.Quaternion q ) => string.Join( ",", new[] { q.X, q.Y, q.Z, q.W }.Select( v => v.ToString( "0.######", System.Globalization.CultureInfo.InvariantCulture ) ) );
            string P( System.Numerics.Vector3 v ) => string.Join( ",", new[] { v.X, v.Y, v.Z }.Select( f => f.ToString( "0.####", System.Globalization.CultureInfo.InvariantCulture ) ) );
            var viewmodel = new PrefabObject { Name = "viewmodel", Tags = "viewmodel" };
            viewmodel.Components.Add( PrefabBuilder.ModelRenderer( firstPersonModel ) );
            viewmodel.Components.Add( new PrefabComponent( "WeaponImporter.WeaponViewmodel", new Dictionary<string, JsonNode>
            {
                ["__enabled"] = true,
                ["FirstPerson"] = true,
                ["HideWorldWeapon"] = true,
                ["CameraBone"] = firstPerson.CameraBone.Length > 0 ? EngineNames.Bone( firstPerson.CameraBone ) : "",
                ["CameraAxes"] = Q( firstPerson.CameraAxes ),
                ["EyeInModel"] = new JsonObject { ["Position"] = P( firstPerson.Eye.Pos ), ["Rotation"] = Q( firstPerson.Eye.Rot ), ["Scale"] = "1,1,1" },
                ["Offset"] = new JsonObject { ["Position"] = "0,0,0", ["Rotation"] = "0,0,0,1", ["Scale"] = "1,1,1" },
            } ) );
            root.Children.Add( viewmodel );
        }
        return PrefabBuilder.Build( prefabPath, root );
    }

    /// <summary>Roles that play this clip as one of their variants.</summary>
    private static IEnumerable<AnimationRole> RolesOfVariant( WeaponSetup setup, string clip )
        => setup.WeaponAnimations.Where( kv => kv.Value.Variants.Contains( clip ) ).Select( kv => kv.Key );

    /// <summary>Sequence names of a role's variant clips.</summary>
    public static IReadOnlyList<string> VariantSequences( WeaponSetup setup, AnimationRole role )
        => setup.WeaponAnimations.TryGetValue( role, out var b )
            ? b.Variants.Where( v => !string.IsNullOrEmpty( v ) && v != b.Clip ).Distinct().Select( v => EngineNames.Sequence( v ) ).ToList()
            : Array.Empty<string>();

    /// <summary>Sequence name the baked model uses for each mapped role.</summary>
    public static Dictionary<AnimationRole, string> SequencesByRole( WeaponSetup setup )
        => setup.WeaponAnimations.Where( kv => !string.IsNullOrEmpty( kv.Value.Clip ) ).ToDictionary( kv => kv.Key, kv => EngineNames.Sequence( kv.Value.Clip ) );

    /// <summary>
    /// Everything <c>WeaponHold</c> needs, as property values. The prefab and the editor preview
    /// both use this, so the preview behaves exactly like the game.
    /// </summary>
    public static Dictionary<string, JsonNode> HoldValues( ImportSession session, Dictionary<AnimationRole, string> sequenceByRole = null )
    {
        var setup = session.Setup;
        sequenceByRole ??= SequencesByRole( setup );
        var baked = setup.Baked ?? new BakedGrip();
        string Tx( float[] a ) => a is { Length: 7 } ? string.Join( ",", a.Select( v => v.ToString( "0.######", System.Globalization.CultureInfo.InvariantCulture ) ) ) : "";
        string Vec( float[] a ) => a is { Length: 3 } ? string.Join( ",", a.Select( v => v.ToString( "0.####", System.Globalization.CultureInfo.InvariantCulture ) ) ) : "0,0,0";
        string Fingers( Dictionary<string, float[]> f ) => string.Join( ";", f.Select( kv => kv.Key + "=" + string.Join( ",", kv.Value.Select( v => v.ToString( "0.######", System.Globalization.CultureInfo.InvariantCulture ) ) ) ) );

        var contacts = new JsonObject();
        foreach ( var (role, track) in setup.Contacts )
        {
            var keys = new JsonArray();
            foreach ( var k in track.Keys.OrderBy( k => k.Time ) )
            {
                var key = new JsonObject { ["hand"] = k.Hand == Core.Hands.Side.Left ? "L" : "R", ["t"] = Math.Round( k.Time, 4 ), ["lock"] = k.State == ContactState.Locked, ["blend"] = Math.Round( k.Blend, 3 ) };
                if ( !string.IsNullOrEmpty( k.Follow ) ) key["follow"] = EngineNames.Bone( k.Follow );
                if ( k.Offset is { Length: 7 } ) key["offset"] = new JsonArray( k.Offset.Select( v => (JsonNode)Math.Round( v, 5 ) ).ToArray() );
                keys.Add( key );
            }
            contacts[role.ToString().ToLowerInvariant()] = keys;
        }

        var actions = new JsonObject();
        foreach ( var role in AnimationRoles.All.Prepend( AnimationRole.Idle ).Distinct() )
        {
            sequenceByRole.TryGetValue( role, out var seq );
            var seconds = ActionTiming.Seconds( setup, session.Analysis?.Asset, role );
            var trigger = AnimationRoles.GraphTrigger( role ) ?? "";
            if ( seq is null && trigger.Length == 0 && role != AnimationRole.Idle && !setup.Contacts.ContainsKey( role ) )
                continue;
            var action = new JsonObject { ["seconds"] = Math.Round( MathF.Max( seconds, 0.05f ), 3 ), ["sequence"] = seq ?? "", ["trigger"] = role == AnimationRole.Idle ? "" : trigger };
            if ( seq is not null && VariantSequences( setup, role ) is { Count: > 0 } variants )
                action["variants"] = new JsonArray( variants.Select( v => (JsonNode)v ).ToArray() );
            actions[role.ToString().ToLowerInvariant()] = action;
        }

        // Replacement character animations (upper-body overlay in WeaponHold).
        var characterActions = new JsonObject();
        foreach ( var (role, tp) in setup.ThirdPerson )
            if ( tp.Source == CharacterAnimationSource.Sequence && !string.IsNullOrEmpty( tp.Sequence ) )
                characterActions[role.ToString().ToLowerInvariant()] = new JsonObject { ["model"] = tp.Model ?? "", ["sequence"] = tp.Sequence, ["blend"] = 0.2 };
        // The baked corrected clips, when chosen, for actions the user didn't give their own animation.
        if ( setup.UseCorrectedAnimations && !string.IsNullOrEmpty( setup.CorrectedModel ) )
            foreach ( var (role, sequence) in setup.CorrectedClips )
            {
                var key = role.ToString().ToLowerInvariant();
                if ( role != AnimationRole.Idle && !characterActions.ContainsKey( key ) )
                    characterActions[key] = new JsonObject { ["model"] = setup.CorrectedModel, ["sequence"] = sequence, ["blend"] = 0.15 };
            }

        return new Dictionary<string, JsonNode>
        {
            ["__enabled"] = true,
            ["CharacterActions"] = characterActions.Count > 0 ? characterActions.ToJsonString() : "",
            ["HoldBone"] = baked.HoldBone,
            ["HoldType"] = setup.EffectiveHoldType,
            ["WeaponInHold"] = Tx( baked.WeaponInHold ),
            ["RightHand"] = Tx( baked.RightHand ),
            ["LeftHand"] = setup.UseSupportHand && baked.LeftHand is not null ? Tx( baked.LeftHand ) : "",
            ["RightFingers"] = Fingers( baked.RightFingers ),
            ["LeftFingers"] = Fingers( baked.LeftFingers ),
            ["Contacts"] = contacts.ToJsonString(),
            ["Actions"] = actions.ToJsonString(),
            ["Smoothing"] = setup.Ik.Smoothing,
            ["ReachSlack"] = setup.Ik.ReachSlack,
            ["SupportHand"] = setup.UseSupportHand,
            ["RightElbowHint"] = Vec( baked.RightElbow ),
            ["LeftElbowHint"] = setup.UseSupportHand ? Vec( baked.LeftElbow ) : "0,0,0",
        };
    }

    /// <summary>Applies <see cref="HoldValues"/> to a live component (editor preview).</summary>
    public static void ConfigureHold( WeaponImporter.WeaponHold hold, ImportSession session )
    {
        var v = HoldValues( session );
        string S( string key ) => v[key]?.GetValue<string>() ?? "";
        hold.HoldBone = S( "HoldBone" );
        hold.HoldType = v["HoldType"]!.GetValue<int>();
        hold.WeaponInHold = S( "WeaponInHold" );
        hold.RightHand = S( "RightHand" );
        hold.LeftHand = S( "LeftHand" );
        hold.RightFingers = S( "RightFingers" );
        hold.LeftFingers = S( "LeftFingers" );
        hold.Contacts = S( "Contacts" );
        hold.Actions = S( "Actions" );
        hold.CharacterActions = S( "CharacterActions" );
        hold.Smoothing = v["Smoothing"]!.GetValue<float>();
        hold.ReachSlack = v["ReachSlack"]!.GetValue<float>();
        hold.SupportHand = v["SupportHand"]!.GetValue<bool>();
        hold.RightElbowHint = Vector3.Parse( S( "RightElbowHint" ) );
        hold.LeftElbowHint = Vector3.Parse( S( "LeftElbowHint" ) );
    }
}

/// <summary>Files and references produced by the geometry/material export step.</summary>
public sealed class ExportedWeapon
{
    public ExportRig Rig { get; set; }
    public string MeshFile { get; set; } = "";
    /// <summary>Files the export already wrote to disk (materials, textures).</summary>
    public List<string> WrittenFiles { get; } = new();
    public List<(string Path, string Text)> TextFiles { get; } = new();
    public List<(string Path, byte[] Bytes)> BinaryFiles { get; } = new();
    public List<(string From, string To)> MaterialRemaps { get; } = new();
    public List<ExportedClip> Clips { get; } = new();
    public List<string> Notes { get; } = new();

    /// <summary>First-person viewmodel (null when not exported).</summary>
    public FirstPersonRig FirstPerson { get; set; }
    public string FirstPersonMeshFile { get; set; } = "";
    public List<(string From, string To)> FirstPersonMaterialRemaps { get; } = new();
    public List<ExportedClip> FirstPersonClips { get; } = new();
}

public sealed record ExportedClip( string Clip, string Sequence, string File, int FrameCount, float Fps );
