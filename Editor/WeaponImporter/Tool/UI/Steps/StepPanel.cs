using Editor;
using Sandbox;

namespace WeaponImporter.Tool.UI;

/// <summary>
/// One step of the workflow in the left column. Steps build their cards from the session and
/// rebuild only when their structure changes (a new weapon, a different part list...); value
/// changes just run the registered refreshers, so a slider being dragged is never destroyed.
/// Rebuilds are deferred to the next frame so a widget is never deleted inside its own callback.
/// </summary>
public abstract class StepPanel : Widget
{
	protected readonly ImporterController C;
	private readonly List<Action> _refreshers = new();
	private string _builtKey;
	private bool _dirty = true;

	protected StepPanel( Widget parent, ImporterController controller ) : base( parent )
	{
		C = controller;
		Layout = Layout.Column();
		Layout.Spacing = 10;
		controller.Changed += MarkDirty;
		controller.SessionReplaced += ForceRebuild;
	}

	public abstract string Title { get; }

	/// <summary>Anything that, when different, needs new widgets rather than new values.</summary>
	protected abstract string StructureKey();

	protected abstract void Build();

	public void MarkDirty() => _dirty = true;

	public void ForceRebuild()
	{
		_builtKey = null;
		_dirty = true;
	}

	/// <summary>Called every frame by the window while this step is visible.</summary>
	public void Flush()
	{
		if ( !_dirty )
			return;
		_dirty = false;
		string key;
		try
		{
			key = C.Session?.Setup is null ? "" : StructureKey();
		}
		catch ( Exception )
		{
			key = Guid.NewGuid().ToString();
		}
		if ( key != _builtKey )
		{
			_builtKey = key;
			Rebuild();
			return;
		}
		foreach ( var refresh in _refreshers.ToArray() )
		{
			try
			{
				refresh();
			}
			catch ( Exception e )
			{
				Log.Warning( $"[weapon importer] refresh failed: {e.Message}" );
			}
		}
	}

	private void Rebuild()
	{
		Layout.Clear( true );
		_refreshers.Clear();
		if ( C.Session?.Setup is null )
			return;
		try
		{
			Build();
		}
		catch ( Exception e )
		{
			Log.Warning( $"[weapon importer] {Title} panel failed: {e}" );
			Layout.Add( UiStyle.Colored( new Label( $"This panel could not be shown: {e.Message}", this ) { WordWrap = true }, Theme.Red ) );
		}
		Layout.AddStretchCell();
	}

	/// <summary>Registers a refresher and runs it once now.</summary>
	protected void Bind( Action refresh )
	{
		_refreshers.Add( refresh );
		refresh();
	}

	protected Card AddCard( string icon, string title, out Layout header, string tooltip = null )
	{
		var card = Layout.Add( new Card( this ) );
		header = card.Header( icon, title, tooltip );
		return card;
	}

	/// <summary>A labelled slider with a live value readout.</summary>
	protected FloatSlider Slider( Widget owner, Layout parent, string caption, string tooltip, float min, float max, float value, Func<float, string> format, Action<float> edited, float step = 0f )
	{
		var row = UiStyle.FieldRow( owner, parent, caption, tooltip );
		var slider = row.Add( new FloatSlider( owner ) { Minimum = min, Maximum = max, Value = value, ToolTip = tooltip, FixedHeight = UiStyle.ControlHeight }, 1 );
		if ( step > 0 )
			slider.Step = step;
		var readout = row.Add( UiStyle.Muted( new Label( format( value ), owner ) { FixedWidth = UiStyle.PillColumn, FixedHeight = UiStyle.ControlHeight, Alignment = TextFlag.RightCenter } ) );
		slider.OnValueEdited = () =>
		{
			readout.Text = format( slider.Value );
			try
			{
				edited( slider.Value );
			}
			catch ( Exception e )
			{
				C.SetStatus( e.Message, Theme.Red );
			}
		};
		return slider;
	}

	/// <summary>A framed dropdown (framed like mocap inputs).</summary>
	protected ComboBox Combo( Widget owner, Layout row, string tooltip, float width = 0 )
	{
		var combo = UiStyle.Framed( new ComboBox( owner ) { ToolTip = tooltip } );
		if ( width > 0 )
			combo.FixedWidth = width;
		else
			combo.MinimumWidth = 60; // long bone/clip names must not widen the column
		combo.MaxVisibleItems = 24;
		row.Add( combo, width > 0 ? 0 : 1 );
		return combo;
	}

	/// <summary>Runs a UI handler, turning exceptions into a red status line.</summary>
	protected void Safe( Action action )
	{
		try
		{
			action();
		}
		catch ( Exception e )
		{
			Log.Warning( $"[weapon importer] {e}" );
			C.SetStatus( e.Message, Theme.Red );
		}
	}

	public override void OnDestroyed()
	{
		C.Changed -= MarkDirty;
		C.SessionReplaced -= ForceRebuild;
		base.OnDestroyed();
	}
}
