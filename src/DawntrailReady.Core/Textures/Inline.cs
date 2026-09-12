namespace DawntrailReady.Core.Textures;

/// <summary>
/// Stands in for <see cref="Task.Run(Action)"/> in the ported TexTools texture code. TexTools spreads texture work over
/// the thread pool (one task per image row); inside the game that competes with the game and other plugins at normal
/// priority. These run the same work on the calling thread, which is DawntrailReady's own low-priority worker. Every
/// pixel and block is computed on its own, so the results are identical.
/// </summary>
internal static class Inline
{
    public static Task Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public static Task<T> Run<T>(Func<T> func) => Task.FromResult(func());

    public static Task<T> Run<T>(Func<Task<T>> func) => func();
}
