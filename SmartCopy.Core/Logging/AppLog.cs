using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartCopy.Core.Logging;

/// <summary>
/// Process-wide service locator for Microsoft.Extensions.Logging.
/// Defaults to <see cref="NullLoggerFactory.Instance"/> so Core classes and unit tests work with zero configuration.
/// Call <see cref="Configure"/> once at application startup (before any stores are constructed) to route log output to the real sinks.
/// </summary>
public static class AppLog
{
    private static volatile ILoggerFactory _factory = NullLoggerFactory.Instance;

    /// <summary>
    /// Replaces the active factory. Safe to call from the UI thread before child objects are created. 
    /// The caller retains ownership and is responsible for disposing the factory.
    /// </summary>
    public static void Configure(ILoggerFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>Creates a typed logger from the active factory.</summary>
    public static ILogger<T> CreateLogger<T>() => _factory.CreateLogger<T>();

    /// <summary>
    /// Resets the factory to <see cref="NullLoggerFactory.Instance"/>.
    /// Useful in tests that configure a real factory and want to clean up afterward.
    /// </summary>
    public static void Reset() => _factory = NullLoggerFactory.Instance;
}
