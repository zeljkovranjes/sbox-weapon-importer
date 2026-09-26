using Editor;
using Sandbox;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Setup;

namespace WeaponImporter.Tool.UI;

/// <summary>Step 3: which clip plays for each action, and what the character plays in third person.</summary>
public sealed class AnimationsStep : StepPanel
{
	public AnimationsStep( Widget parent, ImporterController controller ) : base( parent, controller )
	{
		controller.RoleChanged += MarkDirty;
	}

	public override string Title => "Animations";

	protected override string StructureKey()
	{
		var s = C.Setup;
		var weapon = string.Join( ",", s.WeaponAnimations.OrderBy( kv => kv.Key ).Select( kv => $"{kv.Key}={kv.Value.Clip}:{kv.Value.Manual}:{string.Join( "+", kv.Value.Variants )}" ) ) + s.Type;
		var third = string.Join( ",", s.ThirdPerson.OrderBy( kv => kv.Key ).Select( kv => $"{kv.Key}={kv.Value.Source}:{kv.Value.Sequence}" ) );
		var splits = string.Join( ",", C.Session.TakeParts.Select( kv => $"{kv.Key}:{kv.Value.Count}" ) ) + string.Join( ",", s.TakeSplits.Select( kv => $"{kv.Key}={string.Join( "/", kv.Value )}" ) );
		return $"{weapon}|{third}|{C.BakedModelPath is null}|{C.Analysis.Asset.Clips.Count}|{splits}";
	}

	protected override void Build()
	{
		var s = C.Setup;
		var clips = C.Analysis.Asset.Clips;
		var card = AddCard( "movie", "Animations", out var header, "Click a row to preview it" );
		header.AddStretchCell();
		var mapped = s.WeaponAnimations.Count( kv => !string.IsNullOrEmpty( kv.Value.Clip ) );
		header.Add( new Pill( card, $"{mapped} / {clips.Count} CLIPS", Theme.TextLight, "Roles with a weapon clip / clips in the file" ) );

		if ( clips.Count == 0 )
			card.Layout.Add( UiStyle.Muted( new Label( "This model has no animations. The character's own animations still play for every action.", card ) { WordWrap = true }, small: true ) );
		else if ( C.BakedModelPath is null && mapped > 0 )
			card.Layout.Add( UiStyle.Muted( new Label( "Watch the weapon's own clips in the First person view; in third person they play on the weapon after Bake.", card ) { WordWrap = true }, small: true ) );

		// Column captions, on the same grid as the rows below.
		var captions = card.Layout.AddRow();
		captions.Margin = new Sandbox.UI.Margin( 8, 0, 4, 0 );
		captions.Spacing = UiStyle.RowSpacing;
		captions.Add( new SectionCaption( card, "Action", RoleColumn ) );
		captions.Add( new SectionCaption( card, "Weapon clip", 0 ), 1 );
		captions.Add( new SectionCaption( card, "3rd", UiStyle.PillColumn + UiStyle.RowSpacing + UiStyle.ControlHeight, TextFlag.RightCenter ) );

		foreach ( var role in AnimationRoles.All )
		{
			// Only the actions this kind of weapon has (no reloads for a knife, no block for a rifle),
			// plus any role that already has a clip.
			if ( !AnimationRoles.AppliesTo( role, s.Type ) && !s.WeaponAnimations.ContainsKey( role ) )
				continue;
			var row = card.Layout.Add( new RoleRow( card, C, role ) );
			BuildRow( row, role );
			Bind( () => row.Selected = C.Role == role );
		}

		BuildTakes();
		BuildRetarget();
	}

	/// <summary>Takes holding several actions on one timeline, split into one clip per action.</summary>
	private void BuildTakes()
	{
		var session = C.Session;
		var takes = session.SplittableTakes.ToList();
		if ( takes.Count == 0 )
			return;
		var card = AddCard( "content_cut", "Actions in one take", out _, "Takes that hold several actions one after another are split into one clip per action" );
		card.Layout.Add( UiStyle.Muted( new Label( "Each part is a clip you can pick above. Type the frames where actions start to split differently.", card ) { WordWrap = true }, small: true ) );
		foreach ( var take in takes )
		{
			var clip = session.SourceAsset.FindClip( take );
			session.TakeParts.TryGetValue( take, out var parts );
			var manual = C.Setup.TakeSplits.ContainsKey( take );
			var row = UiStyle.FieldRow( card, card.Layout, ShortClip( take ), $"{take}: {clip?.FrameCount ?? 0} frames" );
			var starts = parts is null ? "" : string.Join( ", ", parts.Skip( 1 ).Select( p => p.Start ) );
			var edit = row.Add( UiStyle.Framed( new LineEdit( card ) { Text = starts, PlaceholderText = "not split", ToolTip = "Frames where each action starts, separated by commas; Enter applies" } ), 1 );
			edit.ReturnPressed += () => Safe( () =>
			{
				var frames = new List<int>();
				foreach ( var part in edit.Text.Split( new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries ) )
				{
					if ( !int.TryParse( part, out var f ) || f <= 0 || f >= (clip?.FrameCount ?? 0) )
					{
						C.SetStatus( $"'{part}' is not a frame inside {take} (1 to {(clip?.FrameCount ?? 1) - 1}).", Theme.Red );
						return;
					}
					frames.Add( f );
				}
				Split( take, frames );
			} );
			row.Add( new Pill( card, parts is null ? "WHOLE" : $"{parts.Count} PARTS", manual ? Theme.Blue : Theme.TextLight, manual ? "Split by you" : "Split automatically" ) );
			if ( manual )
				row.Add( UiStyle.Icon( card, "auto_fix_high", () => Split( take, null ), "Split automatically again" ) );
			if ( parts is not null )
				row.Add( UiStyle.Icon( card, "block", () => Split( take, new List<int>() ), "Keep this take whole" ) );
		}
	}

	private void Split( string take, List<int> starts )
	{
		var session = C.Session;
		_ = C.RunAsync( "Splitting takes", ( p, c ) => session.SetTakeSplitsAsync( take, starts, p, c ) );
	}

	private void BuildRetarget()
	{
		var card = AddCard( "sync_alt", "Third-person animation", out _, "Bring your own character animations (FBX / BVH) for the third-person actions" );
		if ( RetargeterBridge.IsInstalled )
		{
			card.Layout.Add( UiStyle.Muted( new Label( "Retarget a character animation onto the s&box character, then pick its sequence with the person button on a row.", card ) { WordWrap = true }, small: true ) );
			var row = card.Layout.AddRow();
			row.Add( UiStyle.Secondary( card, "Retarget third-person animation…", "sync_alt", Retarget, "Open the Humanoid Retargeter with animation files (FBX, BVH)" ) );
			row.AddStretchCell();
		}
		else
		{
			var link = card.Layout.Add( new Label( $"Retargeting your own animations needs the <a href=\"{RetargeterBridge.InstallUrl}\" style=\"color: {Theme.Green.Hex};\">Humanoid Retargeter</a>. The character's animgraph works without it.", card ) { WordWrap = true, OpenExternalLinks = true, ToolTip = RetargeterBridge.InstallUrl } );
			link.SetStyles( $"color: {Theme.TextLight.Hex}; font-size: 11px;" );
		}
	}

	private void Retarget()
	{
		var path = EditorUtility.OpenFileDialog( "Animation to retarget", "Animations (*.fbx *.bvh)", null );
		if ( string.IsNullOrEmpty( path ) )
			return;
		Safe( () =>
		{
			if ( RetargeterBridge.Open( new[] { path } ) )
				C.SetStatus( $"Opened {System.IO.Path.GetFileName( path )} in the Humanoid Retargeter.", Theme.Green );
			else
				C.SetStatus( "The Humanoid Retargeter could not be opened.", Theme.Red );
		} );
	}

	private void BuildRow( RoleRow row, AnimationRole role )
	{
		var s = C.Setup;
		var layout = row.Layout;
		var label = layout.Add( new Label( AnimationRoles.Label( role, s.Type ), row ) { FixedWidth = RoleColumn, FixedHeight = UiStyle.ControlHeight, Alignment = TextFlag.LeftCenter } );
		if ( C.Role == role )
			UiStyle.Bold( label );

		s.WeaponAnimations.TryGetValue( role, out var binding );
		var hasClip = binding is not null && !string.IsNullOrEmpty( binding.Clip );
		var fallback = !hasClip && AnimationRoles.Fallback( role ) is { } f && s.WeaponAnimations.TryGetValue( f, out var fb ) && !string.IsNullOrEmpty( fb.Clip ) ? f : (AnimationRole?)null;
		var current = hasClip ? ShortClip( binding.Clip ) : fallback is { } fr ? $"uses {AnimationRoles.Label( fr, s.Type )}" : "None";
		var more = hasClip && binding.Variants.Count > 0 ? $" +{binding.Variants.Count}" : "";
		var tip = hasClip ? $"Weapon clip for {AnimationRoles.Label( role, s.Type )}: {binding.Clip}.{(more.Length > 0 ? $" Played in turn with {string.Join( ", ", binding.Variants )}." : "")} Click to change." : $"No weapon clip for {AnimationRoles.Label( role, s.Type )}. Click to pick one.";
		layout.Add( new ClickField( row, current + more, "movie", hasClip ? Theme.Green : Theme.TextLight, () => ChangeWeaponClip( role ), tip ) { Muted = !hasClip }, 1 );

		if ( hasClip )
			layout.Add( binding.Manual ? new Pill( row, "EDITED", Theme.Blue, "You picked this clip", column: true ) : new ConfidencePill( row, binding.Confidence, false, C.Analysis.AssignedAnimations.TryGetValue( role, out var guess ) ? guess.Reason : null ) );
		else
			layout.Add( new Pill( row, "", Theme.TextLight, column: true ) );

		s.ThirdPerson.TryGetValue( role, out var tp );
		tp ??= new CharacterAnimation();
		var custom = tp.Source != CharacterAnimationSource.Graph;
		var third = layout.Add( UiStyle.Icon( row, tp.Source == CharacterAnimationSource.None ? "person_off" : "accessibility_new", () => ChangeThirdPerson( role ),
			$"Third person: {ThirdPersonText( role, tp )}{(tp.Manual ? " (set by you)" : "")}. Click to change." ) );
		if ( custom )
			third.Foreground = Theme.Blue;
	}

	private const float RoleColumn = 92f;

	/// <summary>Clip names often carry the armature ("PistolArmature|Reload"); the part after the bar reads better.</summary>
	private static string ShortClip( string clip )
	{
		var bar = clip.LastIndexOf( '|' );
		return bar >= 0 && bar < clip.Length - 1 ? clip[(bar + 1)..] : clip;
	}

	private static string ThirdPersonText( AnimationRole role, CharacterAnimation tp )
	{
		if ( tp.Source == CharacterAnimationSource.Graph )
		{
			var trigger = AnimationRoles.GraphTrigger( role );
			return trigger is null ? "Character animgraph" : $"Character animgraph · {trigger}";
		}
		if ( tp.Source == CharacterAnimationSource.Sequence )
			return string.IsNullOrEmpty( tp.Model ) ? $"{tp.Sequence} · character" : $"{tp.Sequence} · {System.IO.Path.GetFileNameWithoutExtension( tp.Model )}";
		return "None";
	}

	private void ChangeWeaponClip( AnimationRole role )
	{
		C.SelectRole( role );
		var s = C.Setup;
		s.WeaponAnimations.TryGetValue( role, out var binding );
		// Search list: every clip in the file, the ones that look like this role first.
		var items = new List<PickerItem>();
		foreach ( var clip in C.Analysis.Asset.Clips )
		{
			var guess = C.Analysis.Animations.FirstOrDefault( g => g.Animation == clip.Name );
			var suggested = guess is not null && guess.Role == role;
			var looksLike = guess is not null && guess.Role != AnimationRole.Unknown ? $"{AnimationRoles.Label( guess.Role )}? · " : "";
			items.Add( new PickerItem( clip.Name, clip.Name, $"{looksLike}{clip.Duration:0.00} s", suggested, binding?.Clip == clip.Name ) );
		}
		items.Add( new PickerItem( "", "None", "no weapon clip", Current: binding is null || string.IsNullOrEmpty( binding.Clip ) ) );
		// Attacks can play several clips in turn: add one as a variant, or drop the variants.
		if ( AnimationRoles.HasVariants( role ) && binding is not null && !string.IsNullOrEmpty( binding.Clip ) )
		{
			foreach ( var clip in C.Analysis.Asset.Clips.Where( c => c.Name != binding.Clip && !binding.Variants.Contains( c.Name ) ) )
				items.Add( new PickerItem( VariantKey + clip.Name, $"+ {clip.Name}", "also play this one, in turn", false, false ) );
			if ( binding.Variants.Count > 0 )
				items.Add( new PickerItem( VariantKey, "Only the first clip", $"stop playing {binding.Variants.Count} more in turn", false, false ) );
		}
		new SearchPicker( this, $"Weapon clip for {AnimationRoles.Label( role, s.Type )}", items, name =>
		{
			if ( name is not null && name.StartsWith( VariantKey, StringComparison.Ordinal ) )
				SetVariant( role, name[VariantKey.Length..] );
			else
				SetClip( role, string.IsNullOrEmpty( name ) ? null : name );
		} ).Open();
	}

	private const string VariantKey = "\u0001variant:";

	/// <summary>Adds a clip played in turn with the role's clip; an empty name removes them all.</summary>
	private void SetVariant( AnimationRole role, string clip )
	{
		Safe( () =>
		{
			var s = C.Setup;
			if ( !s.WeaponAnimations.TryGetValue( role, out var binding ) )
				return;
			if ( string.IsNullOrEmpty( clip ) )
				binding.Variants.Clear();
			else if ( !binding.Variants.Contains( clip ) )
				binding.Variants.Add( clip );
			binding.Manual = true;
			C.Session.Revalidate();
			C.MarkChanged();
			C.SelectRole( role );
			C.SetStatus( string.IsNullOrEmpty( clip ) ? $"{AnimationRoles.Label( role, s.Type )} plays only {binding.Clip}." : $"{AnimationRoles.Label( role, s.Type )} plays {binding.Clip} and {string.Join( ", ", binding.Variants )} in turn.", Theme.Green );
		} );
	}

	private void SetClip( AnimationRole role, string clip )
	{
		Safe( () =>
		{
			var s = C.Setup;
			if ( clip is null )
				s.WeaponAnimations.Remove( role );
			else
				s.WeaponAnimations[role] = new AnimationBinding { Clip = clip, Confidence = 1f, Manual = true, Variants = s.WeaponAnimations.TryGetValue( role, out var old ) ? old.Variants.Where( v => v != clip ).ToList() : new List<string>() };
			C.Session.Revalidate();
			C.MarkChanged();
			C.SelectRole( role );
			C.SetStatus( clip is null ? $"{AnimationRoles.Label( role )} has no weapon clip now." : $"{AnimationRoles.Label( role )} plays {clip}.", Theme.Green );
		} );
	}

	private void ChangeThirdPerson( AnimationRole role )
	{
		C.SelectRole( role );
		var menu = new Menu( this );
		menu.AddHeading( $"Third person · {AnimationRoles.Label( role )}" );
		menu.AddOption( "Character animgraph", "account_tree", () => SetThirdPerson( role, new CharacterAnimation { Source = CharacterAnimationSource.Graph, Manual = true } ) );
		AddCharacterSequences( menu.AddMenu( "Character sequence", "accessibility_new" ), role );
		menu.AddOption( "Sequence from another model…", "folder_open", () => PickSequence( role ) );
		menu.AddSeparator();
		menu.AddOption( "None", "block", () => SetThirdPerson( role, new CharacterAnimation { Source = CharacterAnimationSource.None, Manual = true } ) );
		menu.OpenAtCursor();
	}

	/// <summary>The character's own sequences, grouped by their first name part (there are hundreds).</summary>
	private void AddCharacterSequences( Menu menu, AnimationRole role )
	{
		var model = Model.Load( C.Session.CharacterModel );
		if ( model is null || model.IsError || model.AnimationCount == 0 )
		{
			menu.AddOption( "(no sequences)", "block", null ).Enabled = false;
			return;
		}
		var names = model.AnimationNames.OrderBy( n => n, StringComparer.OrdinalIgnoreCase ).ToList();
		var hint = AnimationRoles.Label( role ).ToLowerInvariant().Split( ' ' )[0];
		var suggested = names.Where( n => n.Contains( hint, StringComparison.OrdinalIgnoreCase ) ).Take( 12 ).ToList();
		foreach ( var n in suggested )
		{
			var name = n;
			menu.AddOption( name, "star", () => SetThirdPerson( role, new CharacterAnimation { Source = CharacterAnimationSource.Sequence, Model = "", Sequence = name, Manual = true } ) );
		}
		if ( suggested.Count > 0 )
			menu.AddSeparator();
		foreach ( var group in names.GroupBy( n => n.Split( '_' )[0] ) )
		{
			var sub = group.Count() == 1 ? menu : menu.AddMenu( $"{group.Key} ({group.Count()})", "folder" );
			foreach ( var n in group )
			{
				var name = n;
				sub.AddOption( name, "movie", () => SetThirdPerson( role, new CharacterAnimation { Source = CharacterAnimationSource.Sequence, Model = "", Sequence = name, Manual = true } ) );
			}
		}
	}

	private void PickSequence( AnimationRole role )
	{
		var picker = AssetPicker.Create( this, AssetType.Model );
		picker.Title = "Model with the sequence";
		picker.OnAssetPicked = assets => Safe( () =>
		{
			var asset = assets?.FirstOrDefault();
			if ( asset is null )
				return;
			var model = Model.Load( asset.Path );
			if ( model is null || model.IsError || model.AnimationCount == 0 )
			{
				C.SetStatus( $"{asset.Name} has no sequences.", Theme.Yellow );
				return;
			}
			var menu = new Menu( this );
			menu.AddHeading( $"{asset.Name} · {model.AnimationCount} sequences" );
			foreach ( var name in model.AnimationNames )
			{
				var n = name;
				menu.AddOption( n, "movie", () => SetThirdPerson( role, new CharacterAnimation { Source = CharacterAnimationSource.Sequence, Model = asset.Path, Sequence = n, Manual = true } ) );
			}
			menu.OpenAtCursor();
		} );
		picker.Show();
	}

	private void SetThirdPerson( AnimationRole role, CharacterAnimation value )
	{
		Safe( () =>
		{
			C.Setup.ThirdPerson[role] = value;
			C.MarkChanged();
			C.SelectRole( role );
		} );
	}

	public override void OnDestroyed()
	{
		C.RoleChanged -= MarkDirty;
		base.OnDestroyed();
	}
}

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
