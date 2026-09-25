using Editor;
using Sandbox;

namespace WeaponImporter.Tool.UI;

/// <summary>One choice in a <see cref="SearchPicker"/>.</summary>
public sealed record PickerItem( string Value, string Label, string Detail = "", bool Suggested = false, bool Current = false );

/// <summary>
/// A popup list with a search box: type to filter (every word must match, anywhere in the
/// name), Enter picks the first match. Suggested items come first. Used for choosing clips.
/// </summary>
public sealed class SearchPicker : PopupWidget
{
	private readonly IReadOnlyList<PickerItem> _items;
	private readonly Action<string> _picked;
	private readonly LineEdit _search;
	private readonly Widget _list;
	private List<PickerItem> _shown = new();

	public SearchPicker( Widget parent, string title, IReadOnlyList<PickerItem> items, Action<string> picked ) : base( parent )
	{
		_items = items;
		_picked = picked;
		FixedWidth = 380;
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 6;
		Layout.Add( UiStyle.Bold( new Label( title, this ) ) );
		_search = Layout.Add( new LineEdit( this ) { PlaceholderText = "Type to search…", FixedHeight = UiStyle.ControlHeight } );
		_search.TextEdited += _ => Fill();
		_search.ReturnPressed += () =>
		{
			if ( _shown.Count > 0 )
				Pick( _shown[0].Value );
		};
		var scroll = Layout.Add( new ScrollArea( this ), 1 );
		scroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		scroll.MaximumHeight = 420;
		scroll.MinimumHeight = Math.Min( 420, 30 * (items.Count + 1) );
		_list = new Widget( scroll ) { Layout = Layout.Column() };
		_list.Layout.Spacing = 1;
		scroll.Canvas = _list;
		Fill();
	}

	/// <summary>Opens at the cursor with the search box focused.</summary>
	public void Open()
	{
		OpenAtCursor();
		_search.Focus();
	}

	private void Fill()
	{
		_list.Layout.Clear( true );
		_shown = Filter( _items, _search.Text );
		foreach ( var item in _shown )
		{
			var value = item.Value;
			var row = _list.Layout.Add( new ClickRow( _list, () => Pick( value ), item.Detail ) );
			row.Selected = item.Current;
			row.Layout.Add( new IconButton( item.Current ? "check" : item.Suggested ? "auto_awesome" : "movie" ) { Background = Color.Transparent, FixedSize = 22, TransparentForMouseEvents = true } );
			row.Layout.Add( new Label( UiStyle.Breakable( item.Label ), row ) { WordWrap = true, MinimumWidth = 20 }, 1 );
			if ( item.Detail.Length > 0 )
				row.Layout.Add( UiStyle.Muted( new Label( item.Detail, row ), small: true ) );
		}
		if ( _shown.Count == 0 )
			_list.Layout.Add( UiStyle.Muted( new Label( "Nothing matches.", _list ), small: true ) );
		_list.Layout.AddStretchCell();
	}

	/// <summary>Items matching a query (every word, anywhere in the name or detail), suggested first.</summary>
	public static List<PickerItem> Filter( IEnumerable<PickerItem> items, string query )
	{
		var words = (query ?? "").ToLowerInvariant().Split( new[] { ' ', '_', '-', '|' }, StringSplitOptions.RemoveEmptyEntries );
		return items
			.Where( i => words.All( w => i.Label.Contains( w, StringComparison.OrdinalIgnoreCase ) || i.Detail.Contains( w, StringComparison.OrdinalIgnoreCase ) ) )
			.OrderByDescending( i => i.Suggested )
			.ToList();
	}

	private void Pick( string value )
	{
		_picked( value );
		Close();
	}
}
