using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.UI.Widgets;

/// <summary>A pill showing how sure the importer is about an automatic choice (always a column pill).</summary>
public sealed class ConfidencePill : Pill
{
	public ConfidencePill( Widget parent, float confidence, bool manual = false, string reason = null )
		: base( parent, UiStyle.ConfidenceText( confidence, manual ), UiStyle.ConfidenceColor( confidence, manual ), column: true )
	{
		SetConfidence( confidence, manual, reason );
	}

	public void SetConfidence( float confidence, bool manual, string reason = null )
	{
		Set( UiStyle.ConfidenceText( confidence, manual ), UiStyle.ConfidenceColor( confidence, manual ) );
		ToolTip = manual
			? "Set by you; re-running the analysis keeps it."
			: $"Detected automatically ({confidence * 100f:0}% sure){(string.IsNullOrEmpty( reason ) ? "" : ": " + reason)}";
	}
}
