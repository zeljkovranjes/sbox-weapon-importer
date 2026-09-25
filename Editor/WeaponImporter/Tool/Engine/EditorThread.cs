using System.Runtime.CompilerServices;
using Sandbox;

namespace WeaponImporter.Tool;

/// <summary>
/// Hop onto the editor's main thread: <c>await EditorThread.SwitchToMainThread();</c>.
/// Asset system, widgets and scene objects must only be touched there.
/// </summary>
public static class EditorThread
{
    public static MainThreadAwaitable SwitchToMainThread() => default;

    public readonly struct MainThreadAwaitable : INotifyCompletion
    {
        public MainThreadAwaitable GetAwaiter() => this;
        public bool IsCompleted => ThreadSafe.IsMainThread;
        public void OnCompleted( Action continuation ) => MainThread.Queue( continuation );
        public void GetResult() { }
    }

    /// <summary>Waits roughly <paramref name="ms"/> milliseconds and returns on the main thread.</summary>
    public static async Task Delay( int ms, CancellationToken cancel = default )
    {
        await Task.Delay( ms, cancel );
        await SwitchToMainThread();
    }

    /// <summary>Lets the editor draw a frame before continuing (splits long main-thread work).</summary>
    public static Task NextFrame( CancellationToken cancel = default ) => Delay( 1, cancel );
}
