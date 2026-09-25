using SkiaSharp;
using WeaponImporter.Core.Analysis;
using WeaponImporter.Core.Generation;
using WeaponImporter.Core.Geometry;
using WeaponImporter.Core.Setup;

namespace WeaponImporter.Tool;

public static partial class WeaponBaker
{
    /// <summary>Model DMX, clip DMX and materials for the canonical weapon (runs off the main thread).</summary>
    private static ExportedWeapon Generate( ImportSession session, string folder, string generated, string name, string assetsRoot )
    {
        var result = new ExportedWeapon();
        var analysis = session.Analysis;
        var setup = session.Setup;

        var keep = new List<string>();
        if ( setup.Muzzle is not null ) keep.Add( setup.Muzzle.Bone );
        if ( setup.Eject is not null ) keep.Add( setup.Eject.Bone );
        keep.AddRange( setup.Parts.Select( p => p.Bone ) );
        keep.AddRange( setup.Contacts.Values.SelectMany( c => c.Keys ).Select( k => k.Follow ) );
        var rig = ExportRig.Build( analysis, keep );
        result.Rig = rig;

        // Mesh + materials.
        var dmx = ModelDmxWriter.WriteWithMaterials( rig.Asset, rig.Triangles, DmxSpace.SourceZUpInches, name );
        result.MeshFile = $"{generated}/{name}.dmx";
        result.TextFiles.Add( (result.MeshFile, dmx.Text) );

        var materialsFolder = $"{folder}/materials";
        void Materials( ModelDmxResult model, List<(string From, string To)> remaps )
        {
            foreach ( var (dmxMaterial, material) in model.Materials )
            {
                var output = MaterialWriter.Write( material, materialsFolder, dmxMaterial );
                var pending = MaterialWriter.WriteFiles( output, assetsRoot );
                foreach ( var (absolute, source, channel) in pending )
                {
                    try
                    {
                        SplitChannel( source, channel, absolute );
                    }
                    catch ( Exception e )
                    {
                        result.Notes.Add( $"Couldn't extract the {channel} channel of {source.Name}: {e.Message}" );
                    }
                }
                result.WrittenFiles.Add( output.VmatPath );
                result.WrittenFiles.AddRange( output.Textures.Select( t => t.RelativePath ) );
                if ( setup.MaxTextureSize > 0 )
                    foreach ( var texture in output.Textures )
                    {
                        var written = System.IO.Path.GetFullPath( System.IO.Path.Combine( assetsRoot, texture.RelativePath.Replace( '/', System.IO.Path.DirectorySeparatorChar ) ) );
                        // Only the bake's own copies: an image already at the output path is the user's source.
                        if ( texture.Source.FilePath is { } src && string.Equals( System.IO.Path.GetFullPath( src ), written, StringComparison.OrdinalIgnoreCase ) )
                            continue;
                        LimitSize( written, setup.MaxTextureSize, result.Notes );
                    }
                // ModelDoc names DMX materials after the faceSet material; point them at the generated vmat.
                remaps.Add( ($"{dmxMaterial}.vmat", output.VmatPath) );
            }
        }
        Materials( dmx, result.MaterialRemaps );

        // Clips the setup uses (each only once).
        var used = setup.WeaponAnimations.Values.Select( b => b.Clip ).Where( c => !string.IsNullOrEmpty( c ) ).Distinct().ToList();
        foreach ( var clipName in used )
        {
            var clip = rig.Asset.FindClip( clipName );
            if ( clip is null || clip.FrameCount == 0 )
            {
                result.Notes.Add( $"Clip '{clipName}' is missing and was skipped." );
                continue;
            }
            var sequence = EngineNames.Sequence( clipName );
            var file = $"{generated}/anim_{sequence}.dmx";
            result.TextFiles.Add( (file, AnimationDmxWriter.Write( rig.Asset.Skeleton, clip, DmxSpace.SourceZUpInches, sequence )) );
            result.Clips.Add( new ExportedClip( clipName, sequence, file, clip.FrameCount, clip.Fps ) );
        }

        // First-person viewmodel (files with their own arms): same clips, everything as authored.
        if ( setup.ExportFirstPerson && FirstPersonRig.Build( analysis, keep ) is { } fp )
        {
            result.FirstPerson = fp;
            var fpName = $"{name}_fp";
            var fpDmx = ModelDmxWriter.WriteWithMaterials( fp.Asset, fp.Triangles, DmxSpace.SourceZUpInches, fpName );
            result.FirstPersonMeshFile = $"{generated}/{fpName}.dmx";
            result.TextFiles.Add( (result.FirstPersonMeshFile, fpDmx.Text) );
            Materials( fpDmx, result.FirstPersonMaterialRemaps );
            foreach ( var clipName in used )
            {
                if ( fp.Asset.FindClip( clipName ) is not { FrameCount: > 0 } clip )
                    continue;
                var sequence = EngineNames.Sequence( clipName );
                var file = $"{generated}/fp_anim_{sequence}.dmx";
                result.TextFiles.Add( (file, AnimationDmxWriter.Write( fp.Asset.Skeleton, clip, DmxSpace.SourceZUpInches, sequence )) );
                result.FirstPersonClips.Add( new ExportedClip( clipName, sequence, file, clip.FrameCount, clip.Fps ) );
            }
        }
        return result;
    }

    /// <summary>
    /// Scales an exported texture down so its longest side is at most <paramref name="max"/>
    /// (same file, same format). TGA files stay as they are (Skia can't write them).
    /// </summary>
    private static void LimitSize( string path, int max, List<string> notes )
    {
        if ( !System.IO.File.Exists( path ) )
            return;
        var ext = System.IO.Path.GetExtension( path ).ToLowerInvariant();
        var format = ext switch { ".png" => SKEncodedImageFormat.Png, ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg, _ => (SKEncodedImageFormat?)null };
        if ( format is null )
            return;
        try
        {
            using var codec = SKCodec.Create( path );
            if ( codec is null || Math.Max( codec.Info.Width, codec.Info.Height ) <= max )
                return;
            using var source = SKBitmap.Decode( codec );
            var scale = (float)max / Math.Max( source.Width, source.Height );
            var info = new SKImageInfo( Math.Max( 1, (int)(source.Width * scale) ), Math.Max( 1, (int)(source.Height * scale) ), source.ColorType, source.AlphaType );
            using var resized = source.Resize( info, SKFilterQuality.High );
            if ( resized is null )
                return;
            using var image = SKImage.FromBitmap( resized );
            using var data = image.Encode( format.Value, 95 );
            codec.Dispose();
            System.IO.File.WriteAllBytes( path, data.ToArray() );
        }
        catch ( Exception e ) when ( e is System.IO.IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException )
        {
            notes.Add( $"{System.IO.Path.GetFileName( path )} kept at full size ({e.Message})." );
        }
    }

    /// <summary>Writes one channel of an image as a greyscale PNG (packed glTF roughness/metalness maps).</summary>
    private static void SplitChannel( TextureRef source, TextureChannel channel, string destination )
    {
        var bytes = source.ReadBytes();
        using var bitmap = SKBitmap.Decode( bytes ) ?? throw new InvalidOperationException( "unreadable image" );
        // Plain RGBA: the engine's texture compiler doesn't read 8-bit greyscale PNGs.
        using var output = new SKBitmap( bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque );
        var pixels = bitmap.Pixels;
        var result = new SKColor[pixels.Length];
        for ( var i = 0; i < pixels.Length; i++ )
        {
            var c = pixels[i];
            var v = channel switch { TextureChannel.R => c.Red, TextureChannel.G => c.Green, TextureChannel.B => c.Blue, TextureChannel.A => c.Alpha, _ => c.Red };
            result[i] = new SKColor( v, v, v, 255 );
        }
        output.Pixels = result;
        System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( destination )! );
        using var image = SKImage.FromBitmap( output );
        using var data = image.Encode( SKEncodedImageFormat.Png, 100 );
        System.IO.File.WriteAllBytes( destination, data.ToArray() );
    }
}
