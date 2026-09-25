using Editor;
using Sandbox;

namespace WeaponImporter.Tool.UI;

/// <summary>
/// The editor's page list: one clickable row per page (icon, name, a status dot when something
/// needs attention). Rows are the controls; there are no separate buttons.
/// </summary>
public sealed class NavList : Widget
{
	public const float RowHeight = 34;

	private readonly List<(string Icon, string Title, string Tip)> _pages = new();
	private readonly Dictionary<int, Color?> _status = new();
	private int _hover = -1;

	public int Selected { get; private set; }
	public Action<int> OnSelected { get; set; }

	public NavList( Widget parent ) : base( parent )
	{
		MouseTracking = true;
		Cursor = CursorShape.Finger;
		FixedWidth = 176;
	}

	public void Add( string icon, string title, string tooltip )
	{
		_pages.Add( (icon, title, tooltip) );
		MinimumHeight = _pages.Count * RowHeight + 8;
		Update();
	}

	/// <summary>Dot beside a page: null hides it (all good), otherwise its colour (yellow, red).</summary>
	public void SetStatus( int page, Color? color )
	{
		if ( _status.TryGetValue( page, out var old ) && old == color )
			return;
		_status[page] = color;
		Update();
	}

	/// <summary>The dot colour of a page (null = none), for tests.</summary>
	public Color? StatusOf( int page ) => _status.TryGetValue( page, out var c ) ? c : null;

	public void Select( int page, bool notify = true )
	{
		page = Math.Clamp( page, 0, Math.Max( 0, _pages.Count - 1 ) );
		Selected = page;
		Update();
		if ( notify )
			OnSelected?.Invoke( page );
	}

	private int RowAt( float y ) => y < 4 ? -1 : (int)((y - 4) / RowHeight) is var i && i < _pages.Count ? i : -1;

	protected override void OnMouseMove( MouseEvent e )
	{
		var row = RowAt( e.LocalPosition.y );
		if ( row != _hover )
		{
			_hover = row;
			ToolTip = row >= 0 ? _pages[row].Tip : null;
			Update();
		}
	}

	protected override void OnMouseLeave()
	{
		_hover = -1;
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton )
			return;
		var row = RowAt( e.LocalPosition.y );
		if ( row >= 0 )
			Select( row );
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		for ( var i = 0; i < _pages.Count; i++ )
		{
			var (icon, title, _) = _pages[i];
			var rect = new Rect( 0, 4 + i * RowHeight, Width, RowHeight - 2 );
			var selected = i == Selected;
			if ( selected || i == _hover )
			{
				Paint.ClearPen();
				Paint.SetBrush( selected ? Theme.ControlBackground.Lighten( .35f ) : Theme.ControlBackground.Lighten( .15f ) );
				Paint.DrawRect( rect, 5 );
			}
			if ( selected )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Green );
				Paint.DrawRect( new Rect( rect.Left, rect.Top + 7, 3, rect.Height - 14 ), 1.5f );
			}
			Paint.SetPen( selected ? Theme.Green : Theme.TextLight );
			Paint.DrawIcon( new Rect( rect.Left + 12, rect.Top, 20, rect.Height ), icon, 16 );
			Paint.SetPen( selected ? Theme.Text : Theme.Text.WithAlpha( .8f ) );
			Paint.SetDefaultFont( 9, selected ? 600 : 400 );
			Paint.DrawText( new Rect( rect.Left + 40, rect.Top, rect.Width - 60, rect.Height ), title, TextFlag.LeftCenter );
			if ( _status.TryGetValue( i, out var status ) && status is { } dot )
			{
				Paint.ClearPen();
				Paint.SetBrush( dot );
				Paint.DrawRect( new Rect( rect.Right - 18, rect.Center.y - 4, 8, 8 ), 4 );
			}
		}
	}
}
