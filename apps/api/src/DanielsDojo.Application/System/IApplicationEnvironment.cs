namespace DanielsDojo.Application.System;

/// <summary>
/// Application-level abstraction over the host environment. This keeps the
/// Application layer free of any dependency on ASP.NET Core hosting types; the
/// host project supplies the implementation.
/// </summary>
public interface IApplicationEnvironment
{
    /// <summary>Gets the current environment name (e.g. Development, Production).</summary>
    string EnvironmentName { get; }
}
