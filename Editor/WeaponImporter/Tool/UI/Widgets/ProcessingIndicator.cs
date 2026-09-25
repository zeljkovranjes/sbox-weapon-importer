using Editor;
using Sandbox;

namespace WeaponImporter.Tool.UI;

/// <summary>
/// Stands in the preview's place while the importer works: a green spinner arc with the step
/// title and the latest progress message below it. Styled like the first-load drop box.
/// </summary>
public sealed class ProcessingIndicator : Widget
{
	private string _title = "Processing…";
	private string _message = "";

	/// <summary>False once work stopped: the last message stays, without the spinner.</summary>
	public bool Busy { get; set; } = true;

	public ProcessingIndicator( Widget parent ) : base( parent )
	{
		MinimumSize = new Vector2( 120, 120 );
	}

	public void SetTitle( string title )
	{
		_title = string.IsNullOrEmpty( title ) ? "Processing…" : title;
		Update();
	}

	public void SetMessage( string message )
	{
		_message = message ?? "";
		Update();
	}

	/// <summary>Called every editor frame to turn the spinner.</summary>
	public void Tick()
	{
		if ( Busy && Visible )
			Update();
	}

	protected override void OnPaint() => DrawInto( LocalRect );

	public void DrawInto( Rect area )
	{
		Paint.Antialiasing = true;
		Paint.SetPen( Theme.ControlBackground.Lighten( .2f ), 1 );
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( area.Shrink( 1 ), 6 );
		var center = area.Center;
		var width = MathF.Min( 360f, area.Width - 40f );
		if ( Busy )
		{
			var angle = (float)(RealTime.Now * 300 % 360);
			var c = new Vector2( center.x, center.y - 30 );
			Paint.SetPen( Color.White.WithAlpha( .08f ), 4 );
			Paint.DrawArc( c, new Vector2( 18, 18 ), 0, 360 );
			Paint.SetPen( Theme.Green, 4 );
			Paint.DrawArc( c, new Vector2( 18, 18 ), angle, 100 );
			Paint.SetDefaultFont( 10, 600 );
			Paint.SetPen( Theme.Text );
			Paint.DrawText( new Rect( center.x - width / 2, center.y + 2, width, 22 ), _title, TextFlag.Center );
		}
		Paint.SetDefaultFont();
		Paint.SetPen( Theme.TextLight );
		Paint.DrawText( new Rect( center.x - width / 2, center.y + (Busy ? 26 : -20), width, 40 ), _message, TextFlag.Center | TextFlag.WordWrap );
	}
}
