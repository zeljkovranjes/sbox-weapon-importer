using Editor;
using Sandbox;

namespace WeaponImporter.EditorTools.UI.Widgets;

/// <summary>One choice in a <see cref="SearchPicker"/>.</summary>
public sealed record PickerItem( string Value, string Label, string Detail = "", bool Suggested = false, bool Current = false );
