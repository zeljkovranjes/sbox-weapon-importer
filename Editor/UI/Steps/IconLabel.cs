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

/// <summary>A tinted 18px icon.</summary>
public sealed class IconLabel : Widget
{
	private readonly string _icon;
	private readonly Color _color;

	public IconLabel( Widget parent, string icon, Color color ) : base( parent )
	{
		_icon = icon;
		_color = color;
		FixedSize = 20;
	}

	protected override void OnPaint()
	{
		Paint.SetPen( _color );
		Paint.DrawIcon( LocalRect, _icon, 16 );
	}
}
