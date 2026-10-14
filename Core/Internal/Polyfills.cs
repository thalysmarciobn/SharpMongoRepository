#if !NET9_0_OR_GREATER

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

/// <summary>
///     Compiler-recognised marker for <c>params</c> collection parameters, shipped in the base class
///     library from .NET 9 onwards. Declaring it here lets the net8.0 build use
///     <c>params ReadOnlySpan&lt;T&gt;</c> overloads, which avoid the array allocation a classic
///     <c>params T[]</c> forces at every call site.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class ParamCollectionAttribute : Attribute;

#endif
