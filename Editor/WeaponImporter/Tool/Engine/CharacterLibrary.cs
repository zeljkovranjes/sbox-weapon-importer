using Sandbox;
using WeaponImporter.Core.Grip;
using WeaponImporter.Core.Ik;
using WeaponImporter.Core.Maths;
using WeaponImporter.Core.Rig;

namespace WeaponImporter.Tool;

/// <summary>The characters grips are fitted to, and how to read their skeletons and poses.</summary>
public static class CharacterLibrary
{
    public const string Human = "models/citizen_human/citizen_human_male.vmdl";
    public const string HumanFemale = "models/citizen_human/citizen_human_female.vmdl";
    public const string Citizen = "models/citizen/citizen.vmdl";

    public static string ModelPath( string character ) => character switch
    {
        "human" or "" or null => Human,
        "human_female" => HumanFemale,
        "citizen" => Citizen,
        _ => character,
    };

    /// <summary>A short label for a character id or custom model path.</summary>
    public static string Label( string character ) => character switch
    {
        "human" or "" or null => "Human",
        "human_female" => "Human (female)",
        "citizen" => "Citizen",
        _ => System.IO.Path.GetFileNameWithoutExtension( character ),
    };

    /// <summary>
    /// Why a model can't be used as the third-person character, or null when it can: grips are
    /// fitted to the character's own hold animations, so it needs an animation graph (the
    /// citizen/human one or a compatible graph with the holdtype parameters) and arms with hands.
    /// </summary>
    public static string Problem( string character )
    {
        var path = ModelPath( character );
        var model = Model.Load( path );
        if ( model is null || model.IsError )
            return $"'{path}' could not be loaded.";
        if ( model.AnimGraph is null && model.AnimationCount == 0 )
            return $"{Label( character )} has no animations (a static body). Third-person grips are fitted to the character's hold animations: use a model that includes the citizen or human animations.";
        if ( GraphFor( model ) is null )
            return $"{Label( character )} has no animation graph and its skeleton doesn't match the citizen or human one. Third-person grips are fitted to the character's hold animations.";
        var rig = CharacterRig.From( SkeletonOf( model ) );
        if ( rig is null )
            return $"{Label( character )} has no recognisable right arm and hand.";
        if ( rig.Left is null )
            return $"{Label( character )} has no recognisable left arm and hand.";
        return null;
    }

    /// <summary>
    /// Bones the citizen and human animgraphs drive by name. A custom body (for example the
    /// citizen mannequin) often has only these: no IK, twist, hold or face helpers.
    /// </summary>
    private static readonly string[] CoreBones =
    {
        "pelvis", "spine_0", "spine_1", "spine_2", "neck_0", "head",
        "clavicle_L", "arm_upper_L", "arm_lower_L", "hand_L", "clavicle_R", "arm_upper_R", "arm_lower_R", "hand_R",
        "leg_upper_L", "leg_lower_L", "ankle_L", "leg_upper_R", "leg_lower_R", "ankle_R",
    };

    /// <summary>
    /// The animgraph a character is posed with: its own, else (custom bodies on the citizen or
    /// human skeleton usually leave it to the player controller) the graph of the built-in
    /// character whose skeleton it shares most. Null when there is neither.
    /// </summary>
    public static AnimationGraph GraphFor( Model model ) => GraphFor( model, out _ );

    public static AnimationGraph GraphFor( Model model, out string borrowedFrom )
    {
        borrowedFrom = null;
        if ( model is null || model.IsError )
            return null;
        if ( model.AnimGraph is { } own )
            return own;
        // A graph plays the model's own sequences: without any there is nothing to borrow for.
        if ( model.AnimationCount == 0 )
            return null;
        var bones = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        for ( var i = 0; i < model.BoneCount; i++ )
            bones.Add( model.GetBoneName( i ) );
        if ( !CoreBones.All( bones.Contains ) )
            return null;
        AnimationGraph best = null;
        var bestShare = -1f;
        foreach ( var path in new[] { Human, Citizen } )
        {
            var builtIn = Model.Load( path );
            if ( builtIn is null || builtIn.IsError || builtIn.AnimGraph is null || builtIn.BoneCount == 0 )
                continue;
            var shared = 0;
            for ( var i = 0; i < builtIn.BoneCount; i++ )
                if ( bones.Contains( builtIn.GetBoneName( i ) ) )
                    shared++;
            var share = shared / (float)builtIn.BoneCount;
            if ( share > bestShare )
            {
                bestShare = share;
                best = builtIn.AnimGraph;
                borrowedFrom = path;
            }
        }
        return best;
    }

    /// <summary>Sets up a character renderer: its model, and the graph it is posed with.</summary>
    public static void Setup( SkinnedModelRenderer body, Model model )
    {
        body.Model = model;
        if ( model?.AnimGraph is null && GraphFor( model ) is { } graph )
            body.AnimationGraph = graph;
        body.UseAnimGraph = true;
        body.PlayAnimationsInEditorScene = true;
    }

    /// <summary>Bind-pose skeleton of a model in model space.</summary>
    public static Skeleton SkeletonOf( Model model )
    {
        var defs = new List<BoneDefinition>( model.BoneCount );
        var names = new string[model.BoneCount];
        for ( var i = 0; i < model.BoneCount; i++ )
            names[i] = model.GetBoneName( i );
        for ( var i = 0; i < model.BoneCount; i++ )
        {
            var parent = model.GetBoneParent( i );
            var world = model.GetBoneTransform( i ).ToCore();
            var local = parent >= 0 ? XForm.ToLocal( model.GetBoneTransform( parent ).ToCore(), world ) : world;
            defs.Add( new BoneDefinition( names[i], parent >= 0 ? names[parent] : null, local ) );
        }
        return Skeleton.Create( defs );
    }

    /// <summary>
    /// The animated pose of a character renderer in model space (animation only, before any
    /// bone overrides), mapped onto <paramref name="skeleton"/> by bone name.
    /// </summary>
    public static CharacterPose Sample( SkinnedModelRenderer body, Skeleton skeleton )
    {
        var locals = skeleton.Bones.Select( b => b.RestLocal ).ToArray();
        var world = new XForm[skeleton.Count];
        var has = new bool[skeleton.Count];
        var root = body.WorldTransform;
        foreach ( var bone in body.Model.Bones.AllBones )
        {
            var i = skeleton.IndexOf( bone.Name );
            if ( i < 0 )
                continue;
            if ( body.TryGetBoneTransformAnimation( bone, out var tx ) )
            {
                world[i] = root.ToLocal( tx ).ToCore();
                has[i] = true;
            }
        }
        for ( var i = 0; i < skeleton.Count; i++ )
        {
            if ( !has[i] )
                continue;
            var p = skeleton[i].ParentIndex;
            locals[i] = p < 0 ? world[i] : has[p] ? XForm.ToLocal( world[p], world[i] ) : locals[i];
        }
        return new CharacterPose( skeleton, locals );
    }
}
