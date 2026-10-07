using Editor;
using Sandbox;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Hands;
using WeaponImporter.EditorTools.Core.Maths;
using WeaponImporter.EditorTools.Core.Setup;
using WeaponImporter.EditorTools.Core.Weapon;
using N = System.Numerics;

namespace WeaponImporter.EditorTools.UI;
using WeaponImporter.EditorTools.Engine;
using WeaponImporter.EditorTools.UI.Widgets;

/// <summary>Reports progress on the main thread (the editor has no synchronization context).</summary>
public sealed class MainThreadProgress : IProgress<string>
{
	private readonly Action<string> _report;

	public MainThreadProgress( Action<string> report ) => _report = report;

	public void Report( string value )
	{
		if ( ThreadSafe.IsMainThread )
			_report( value );
		else
			MainThread.Queue( () => _report( value ) );
	}
}
