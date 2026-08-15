using System;

namespace Huuray;

/// <summary>
/// Base class for everything this library throws. Catch this to catch it all.
/// </summary>
public class HuurayException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">A description of what went wrong.</param>
    public HuurayException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an underlying cause.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public HuurayException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The client is misconfigured — missing credentials, an unusable base URL.
/// Not a response from the API.
/// </summary>
public sealed class HuurayConfigurationException : HuurayException
{
    /// <summary>Creates a configuration exception with a message.</summary>
    /// <param name="message">A description of what is wrong with the configuration.</param>
    public HuurayConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a configuration exception with a message and an underlying cause.</summary>
    /// <param name="message">A description of what is wrong with the configuration.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public HuurayConfigurationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
