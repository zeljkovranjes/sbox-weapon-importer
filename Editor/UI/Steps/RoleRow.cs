using Editor;
using Sandbox;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Setup;

namespace WeaponImporter.EditorTools.UI.Steps;
using WeaponImporter.EditorTools.Engine;
using WeaponImporter.EditorTools.UI.Widgets;

/// <summary>One action in the animation list; clicking it previews the action.</summary>
public sealed class RoleRow : Widget
{
	private readonly ImporterController _c;
	private readonly AnimationRole _role;
	private bool _selected;

	public RoleRow( Widget parent, ImporterController controller, AnimationRole role ) : base( parent )
	{
		_c = controller;
		_role = role;
		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( 8, 3, 4, 3 );
		Layout.Spacing = UiStyle.RowSpacing;
		MouseTracking = true;
		Cursor = CursorShape.Finger;
		ToolTip = $"Preview {AnimationRoles.Label( role )}";
	}

	public bool Selected
	{
		get => _selected;
		set
		{
			if ( _selected == value )
				return;
			_selected = value;
			Update();
		}
	}

	protected override void OnMouseEnter() => Update();
	protected override void OnMouseLeave() => Update();

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			_c.SelectRole( _role );
			e.Accepted = true;
		}
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		if ( _selected )
		{
			Paint.SetPen( Theme.Green.WithAlpha( .6f ), 1 );
			Paint.SetBrush( Theme.Green.WithAlpha( .08f ) );
			Paint.DrawRect( LocalRect.Shrink( .5f ), 4 );
			Paint.ClearPen();
			Paint.SetBrush( Theme.Green );
			Paint.DrawRect( new Rect( 0, 6, 3, Height - 12 ), 1.5f );
		}
		else if ( Paint.HasMouseOver )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.Lighten( .3f ) );
			Paint.DrawRect( LocalRect, 4 );
		}
	}
}
