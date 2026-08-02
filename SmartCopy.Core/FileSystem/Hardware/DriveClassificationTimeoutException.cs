namespace SmartCopy.Core.FileSystem.Hardware;

/// <summary>
/// An attempt that did not finish, as distinct from a drive the classifier cannot describe. Both look
/// like Unknown to a caller, but only the latter is cached — see <see cref="DriveClassificationRegistry"/>.
/// Caller cancellation is neither, and propagates as <see cref="OperationCanceledException"/>.
/// </summary>
internal sealed class DriveClassificationTimeoutException : Exception;
