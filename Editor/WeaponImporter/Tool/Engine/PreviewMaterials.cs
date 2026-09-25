using System.Collections.Concurrent;
using Sandbox;
using SkiaSharp;
using WeaponImporter.Core.Geometry;

namespace WeaponImporter.Tool;

/// <summary>
/// In-memory materials for the preview: each material's base color texture (linked in the file
/// or attached by Attach Textures), or its base color when it has none. Images are decoded off
/// the main thread and cached, so switching pages or characters never decodes twice.
/// </summary>
public static class PreviewMaterials
{
    /// <summary>Largest texture side in the preview (keeps big packs quick and light).</summary>
    public const int MaxSize = 1024;

    private sealed record Decoded( int Width, int Height, byte[] Rgba );

    private static readonly ConcurrentDictionary<string, Decoded> _images = new();

    /// <summary>One material per mesh material (same order); null entries fall back to the default.</summary>
    public static async Task<Material[]> BuildAsync( IReadOnlyList<MaterialInfo> materials, CancellationToken cancel = default )
    {
        var decoded = await Task.Run( () => materials.Select( m => m.BaseColorTexture is { } t ? Decode( t ) : null ).ToArray(), cancel );
        await EditorThread.SwitchToMainThread();
        cancel.ThrowIfCancellationRequested();
        LastReport.Clear();
        var result = new Material[materials.Count];
        for ( var i = 0; i < materials.Count; i++ )
            result[i] = Create( materials[i], decoded[i] );
        return result;
    }

    /// <summary>What the last build gave each material (diagnostics).</summary>
    public static List<string> LastReport { get; } = new();

    private static Material Create( MaterialInfo info, Decoded image )
    {
        // A copy of a configured complex-shader material (a bare Material.Create lacks its setup).
        // Resource names can't carry dots ("Boots.001" reads as an extension).
        var name = System.Text.RegularExpressions.Regex.Replace( info.Name ?? "material", "[^A-Za-z0-9_]", "_" );
        var material = Material.Load( PreviewModels.SurfaceMaterial )?.CreateCopy( $"weapon_importer_preview_{name}" );
        if ( material is null )
            return null;
        var c = info.BaseColor;
        Texture texture;
        if ( image is not null )
            texture = Texture.Create( image.Width, image.Height ).WithData( image.Rgba ).WithMips().Finish();
        else
            texture = Texture.Create( 1, 1 ).WithData( new[] { ToByte( c.X ), ToByte( c.Y ), ToByte( c.Z ), (byte)255 } ).Finish();
        material.Set( "g_tColor", texture );
        if ( image is not null )
            material.Set( "g_vColorTint", new Color( c.X, c.Y, c.Z, 1f ) );
        var back = material.GetTexture( "g_tColor" );
        LastReport.Add( $"{info.Name}: {material.ShaderName} · {(image is null ? "base color" : $"{image.Width}×{image.Height}")} · set {(back is null ? "none" : $"{back.Width}×{back.Height}")}" );
        return material;
    }

    private static byte ToByte( float v ) => (byte)Math.Clamp( (int)MathF.Round( v * 255f ), 0, 255 );

    /// <summary>The image as RGBA (at most <see cref="MaxSize"/>), or null when it can't be read.</summary>
    private static Decoded Decode( TextureRef texture )
    {
        var key = texture.FilePath is { } path
            ? $"{path}|{(File.Exists( path ) ? File.GetLastWriteTimeUtc( path ).Ticks : 0)}"
            : $"{texture.Name}|{texture.Bytes?.Length}";
        if ( _images.TryGetValue( key, out var cached ) )
            return cached;
        Decoded result = null;
        try
        {
            var bytes = texture.ReadBytes();
            using var bitmap = texture.Extension == ".tga" ? Tga.Decode( bytes ) : SKBitmap.Decode( bytes );
            if ( bitmap is not null )
                result = ToRgba( bitmap );
        }
        catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or IndexOutOfRangeException )
        {
            Log.Info( $"[weapon importer] preview: {texture.Name}{texture.Extension} could not be read ({e.Message}); showing the base color." );
        }
        _images[key] = result;
        return result;
    }

    private static Decoded ToRgba( SKBitmap source )
    {
        var scale = MathF.Min( 1f, (float)MaxSize / Math.Max( source.Width, source.Height ) );
        var width = Math.Max( 1, (int)(source.Width * scale) );
        var height = Math.Max( 1, (int)(source.Height * scale) );
        using var rgba = new SKBitmap( width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul );
        using ( var canvas = new SKCanvas( rgba ) )
        using ( var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium } )
            canvas.DrawBitmap( source, new SKRect( 0, 0, width, height ), paint );
        return new Decoded( width, height, rgba.Bytes );
    }

    /// <summary>Truevision TGA (the engine reads it, Skia doesn't): 24/32-bit, raw or RLE.</summary>
    private static class Tga
    {
        public static SKBitmap Decode( byte[] d )
        {
            int idLength = d[0], type = d[2];
            int width = d[12] | d[13] << 8, height = d[14] | d[15] << 8, bpp = d[16] / 8;
            var topDown = (d[17] & 0x20) != 0;
            if ( (type != 2 && type != 10) || (bpp != 3 && bpp != 4) || width == 0 || height == 0 )
                return null;
            var pixels = new byte[width * height * 4];
            var src = 18 + idLength;
            var count = width * height;
            void Put( int i, int at )
            {
                pixels[i * 4] = d[at + 2];
                pixels[i * 4 + 1] = d[at + 1];
                pixels[i * 4 + 2] = d[at];
                pixels[i * 4 + 3] = bpp == 4 ? d[at + 3] : (byte)255;
            }
            for ( var i = 0; i < count; )
            {
                if ( type == 2 )
                {
                    Put( i++, src );
                    src += bpp;
                    continue;
                }
                var header = d[src++];
                var run = (header & 0x7F) + 1;
                if ( (header & 0x80) != 0 )
                {
                    for ( var k = 0; k < run && i < count; k++ )
                        Put( i++, src );
                    src += bpp;
                }
                else
                {
                    for ( var k = 0; k < run && i < count; k++, src += bpp )
                        Put( i++, src );
                }
            }
            if ( !topDown )
            {
                var row = width * 4;
                var tmp = new byte[row];
                for ( var y = 0; y < height / 2; y++ )
                {
                    Buffer.BlockCopy( pixels, y * row, tmp, 0, row );
                    Buffer.BlockCopy( pixels, (height - 1 - y) * row, pixels, y * row, row );
                    Buffer.BlockCopy( tmp, 0, pixels, (height - 1 - y) * row, row );
                }
            }
            var bitmap = new SKBitmap( width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul );
            System.Runtime.InteropServices.Marshal.Copy( pixels, 0, bitmap.GetPixels(), pixels.Length );
            return bitmap;
        }
    }
}
