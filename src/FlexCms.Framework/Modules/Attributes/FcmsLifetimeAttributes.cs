namespace FlexCms.Framework.Modules.Attributes;

/// <summary>
/// Marks a class for automatic registration as a scoped DI service.
/// The framework's module loader scans for this attribute on every loaded
/// module assembly and registers each marked type as
/// <c>services.AddScoped(implementationType)</c> (or against an explicit
/// service type if <see cref="ServiceType"/> is set).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FcmsScopedAttribute : Attribute
{
    /// <summary>
    /// Optional explicit service type to register against. When null, the
    /// implementation type itself is used (no interface mapping).
    /// </summary>
    public Type? ServiceType { get; }

    public FcmsScopedAttribute() { }
    public FcmsScopedAttribute(Type serviceType) => ServiceType = serviceType;
}

/// <summary>Singleton counterpart of <see cref="FcmsScopedAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FcmsSingletonAttribute : Attribute
{
    public Type? ServiceType { get; }

    public FcmsSingletonAttribute() { }
    public FcmsSingletonAttribute(Type serviceType) => ServiceType = serviceType;
}

/// <summary>
/// Marks a class as an <c>IHostedService</c> to be registered via
/// <c>services.AddHostedService</c>. The class must implement
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FcmsHostedServiceAttribute : Attribute { }
