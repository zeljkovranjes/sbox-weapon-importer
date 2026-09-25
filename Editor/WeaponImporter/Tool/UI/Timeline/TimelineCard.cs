using Editor;
using Sandbox;
using WeaponImporter.Core.Analysis;

namespace WeaponImporter.Tool.UI;

/// <summary>Transport for the selected action (play, step, speed, loop, scrub) above its contact and event lanes.</summary>
public sealed class TimelineCard : Card
{
	private readonly ImporterController _c;
	private readonly IconButton _play;
	private readonly IconButton _loop;
	private readonly FloatSlider _scrub;
	private readonly Label _clock;
	private readonly Label _role;
	private readonly TimelineLanes _lanes;
	private bool _scrubbing;

	public TimelineCard( Widget parent, ImporterController controller ) : base( parent )
	{
		_c = controller;
		Layout.Margin = new Sandbox.UI.Margin( 10, 6, 10, 8 );
		Layout.Spacing = 4;

		var transport = Layout.AddRow();
		transport.Spacing = 6;
		_role = transport.Add( UiStyle.Bold( new Label( "Idle", this ) { FixedWidth = TimelineLanes.LabelWidth - 6, FixedHeight = UiStyle.ControlHeight, ToolTip = "The action being previewed; pick another in the preview header or in Animations" } ) );
		_play = transport.Add( UiStyle.Icon( this, "pause", controller.TogglePlay, "Play / pause (Space)" ) );
		transport.Add( UiStyle.Icon( this, "chevron_left", () => controller.StepFrame( -1 ), "Previous frame (Left arrow)" ) );
		transport.Add( UiStyle.Icon( this, "chevron_right", () => controller.StepFrame( 1 ), "Next frame (Right arrow)" ) );
		_scrub = transport.Add( new FloatSlider( this ) { Minimum = 0, Maximum = 1, Value = 0, ToolTip = "Scrub through the action", FixedHeight = UiStyle.ControlHeight }, 1 );
		_scrub.OnValueEdited = () =>
		{
			_scrubbing = true;
			controller.Seek( _scrub.Value );
			_scrubbing = false;
		};
		_clock = transport.Add( UiStyle.Muted( new Label( "0.00 s", this ) { FixedWidth = 92, FixedHeight = UiStyle.ControlHeight, Alignment = TextFlag.RightCenter, ToolTip = "Time in the action / its length" } ) );
		var speed = transport.Add( UiStyle.Framed( new ComboBox( this ) { FixedWidth = 70, ToolTip = "Playback speed" } ) );
		foreach ( var s in new[] { 0.25f, 0.5f, 1f, 2f } )
		{
			var v = s;
			speed.AddItem( $"{s:0.##}×", null, () => controller.Speed = v, selected: s == 1f );
		}
		_loop = transport.Add( UiStyle.Toggle( this, "repeat", true, null, "Loop the action" ) );
		_loop.OnToggled = on => controller.Loop = on;

		_lanes = Layout.Add( new TimelineLanes( this, controller ) );

		controller.RoleChanged += Refresh;
		controller.Changed += Refresh;
	}

	private void Refresh()
	{
		_role.Text = AnimationRoles.Label( _c.Role );
		_lanes.Update();
	}

	/// <summary>Per frame: playhead, clock and play icon.</summary>
	public void Tick()
	{
		var seconds = _c.RoleSeconds( _c.Role );
		if ( !_scrubbing )
			_scrub.Value = _c.Time;
		_clock.Text = $"{_c.Time * seconds:0.00} / {seconds:0.00} s";
		var icon = _c.Playing ? "pause" : "play_arrow";
		if ( _play.Icon != icon )
		{
			_play.Icon = icon;
			_play.Update();
		}
		if ( _role.Text != AnimationRoles.Label( _c.Role ) )
			_role.Text = AnimationRoles.Label( _c.Role );
		_lanes.SetPlayhead( _c.Time );
	}

	public override void OnDestroyed()
	{
		_c.RoleChanged -= Refresh;
		_c.Changed -= Refresh;
		base.OnDestroyed();
	}
}
