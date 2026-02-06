#if NETSTANDARD2_0
// Polyfill to enable records and init-only properties in netstandard2.0
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit;
}
#endif
