// Polyfill so positional records and init setters compile on netstandard2.1 and Unity.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
