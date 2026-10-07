using System.Globalization;
using Editor;
using Sandbox;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Setup;
using WeaponImporter.EditorTools.Core.Weapon;
using N = System.Numerics;

namespace WeaponImporter.EditorTools.UI.Steps;
using WeaponImporter.EditorTools.Engine;
using WeaponImporter.EditorTools.UI.Widgets;

/// <summary>
/// A row that reports hover (to highlight the matching bone in the viewport) and, optionally,
/// clicks and a selected state (green edge, like the animation rows).
/// </summary>
public sealed class HoverRow : Widget
{
	private readonly Action _enter;
	private readonly Action _leave;
	private bool _selected;

	public Action Clicked { get; set; }

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

	public HoverRow( Widget parent, Action enter, Action leave ) : base( parent )
	{
		_enter = enter;
		_leave = leave;
		Layout = Layout.Row();
		Layout.Spacing = UiStyle.RowSpacing;
		MouseTracking = true;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( Clicked is null || !e.LeftMouseButton )
			return;
		Clicked();
		e.Accepted = true;
	}

	protected override void OnMouseEnter()
	{
		_enter?.Invoke();
		Update();
	}

	protected override void OnMouseLeave()
	{
		_leave?.Invoke();
		Update();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		if ( _selected )
		{
			Paint.SetPen( Theme.Green.WithAlpha( .6f ), 1 );
			Paint.SetBrush( Theme.Green.WithAlpha( .08f ) );
			Paint.DrawRect( LocalRect.Shrink( .5f ), UiStyle.Radius );
			return;
		}
		if ( !Paint.HasMouseOver )
			return;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground.Lighten( .3f ) );
		Paint.DrawRect( LocalRect, UiStyle.Radius );
	}
}
