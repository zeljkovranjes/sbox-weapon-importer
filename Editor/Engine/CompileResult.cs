using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.Engine;

/// <summary>Outcome of compiling one generated file.</summary>
public sealed record CompileResult( string Path, bool Success, IReadOnlyList<string> Errors )
{
    public static CompileResult Failed( string path, string error ) => new( path, false, new[] { error } );
}
