#if NETSTANDARD2_0
// Compile-time-only polyfill: netstandard2.0 does not ship these annotations, but the C#
// compiler honours the fully-qualified name for nullable flow analysis. Internal, so it never
// leaks into the plugin's reference surface.
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue)
        {
            ReturnValue = returnValue;
        }

        public bool ReturnValue { get; }
    }
}
#endif
