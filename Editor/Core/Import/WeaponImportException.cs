#nullable enable annotations

using WeaponImporter.EditorTools.Core.Weapon;

namespace WeaponImporter.EditorTools.Core.Import;

/// <summary>An import failure with a message written for the person importing.</summary>
public sealed class WeaponImportException : Exception
{
    public WeaponImportException(string message, Exception? inner = null) : base(message, inner) { }
}
