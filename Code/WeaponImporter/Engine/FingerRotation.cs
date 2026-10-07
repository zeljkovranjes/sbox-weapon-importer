using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WeaponImporter.Engine;

/// <summary>A baked finger bone local rotation.</summary>
public readonly struct FingerRotation
{
    /// <summary>Creates a finger entry.</summary>
    public FingerRotation( string bone, Rotation rotation )
    {
        Bone = bone;
        Rotation = rotation;
    }

    /// <summary>Bone name.</summary>
    public string Bone { get; }

    /// <summary>Rotation relative to the parent bone.</summary>
    public Rotation Rotation { get; }
}
