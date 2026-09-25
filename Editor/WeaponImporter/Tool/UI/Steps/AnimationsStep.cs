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
		var weapon = string.Join( ",", s.WeaponAnimations.OrderBy( kv => kv.Key ).Select( kv => $"{kv.Key}={kv.Value.Clip}:{kv.Value.Manual}" ) );
		var third = string.Join( ",", s.ThirdPerson.OrderBy( kv => kv.Key ).Select( kv => $"{kv.Key}={kv.Value.Source}:{kv.Value.Sequence}" ) );
		return $"{weapon}|{third}|{C.BakedModelPath is null}|{C.Analysis.Asset.Clips.Count}";
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
			var row = card.Layout.Add( new RoleRow( card, C, role ) );
			BuildRow( row, role );
			Bind( () => row.Selected = C.Role == role );
		}

		BuildRetarget();
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
		var label = layout.Add( new Label( AnimationRoles.Label( role ), row ) { FixedWidth = RoleColumn, FixedHeight = UiStyle.ControlHeight, Alignment = TextFlag.LeftCenter } );
		if ( C.Role == role )
			UiStyle.Bold( label );

		s.WeaponAnimations.TryGetValue( role, out var binding );
		var hasClip = binding is not null && !string.IsNullOrEmpty( binding.Clip );
		var fallback = !hasClip && AnimationRoles.Fallback( role ) is { } f && s.WeaponAnimations.TryGetValue( f, out var fb ) && !string.IsNullOrEmpty( fb.Clip ) ? f : (AnimationRole?)null;
		var current = hasClip ? ShortClip( binding.Clip ) : fallback is { } fr ? $"uses {AnimationRoles.Label( fr )}" : "None";
		layout.Add( new ClickField( row, current, "movie", hasClip ? Theme.Green : Theme.TextLight, () => ChangeWeaponClip( role ),
			hasClip ? $"Weapon clip for {AnimationRoles.Label( role )}: {binding.Clip}. Click to change." : $"No weapon clip for {AnimationRoles.Label( role )}. Click to pick one." ) { Muted = !hasClip }, 1 );

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
		var menu = new Menu( this );
		menu.AddHeading( $"Weapon clip for {AnimationRoles.Label( role )}" );
		var clips = C.Analysis.Asset.Clips;
		foreach ( var clip in clips )
		{
			var name = clip.Name;
			var guess = C.Analysis.Animations.FirstOrDefault( g => g.Animation == name );
			var label = guess is not null && guess.Role != AnimationRole.Unknown ? $"{name}   ({AnimationRoles.Label( guess.Role )}?)" : name;
			menu.AddOption( $"{label}  ·  {clip.Duration:0.00} s", binding?.Clip == name ? "check" : "movie", () => SetClip( role, name ) );
		}
		if ( clips.Count > 0 )
			menu.AddSeparator();
		menu.AddOption( "None", binding is null ? "check" : "block", () => SetClip( role, null ) );
		menu.OpenAtCursor();
	}

	private void SetClip( AnimationRole role, string clip )
	{
		Safe( () =>
		{
			var s = C.Setup;
			if ( clip is null )
				s.WeaponAnimations.Remove( role );
			else
				s.WeaponAnimations[role] = new AnimationBinding { Clip = clip, Confidence = 1f, Manual = true };
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
