namespace MBW.GHLinguist;

/// <summary>Represents a failure reported by the native GitHub Linguist runtime.</summary>
/// <remarks>
/// This exception is raised when the native ABI cannot complete an operation, including initialization,
/// ABI incompatibility, unavailable features, and native internal failures. It does not expose native
/// handles or buffers.
/// </remarks>
public class LinguistException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="LinguistException" /> class.</summary>
    /// <param name="message">A description of the native runtime failure.</param>
    /// <example><code>throw new LinguistException("The native runtime returned inconsistent metadata.");</code></example>
    public LinguistException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="LinguistException" /> class with an inner exception.</summary>
    /// <param name="message">A description of the native runtime failure.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    /// <example><code>throw new LinguistException("Invalid UTF-8 returned by the native runtime.", decoderException);</code></example>
    public LinguistException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Represents a Ruby exception captured and copied by the native GitHub Linguist runtime.</summary>
/// <remarks>
/// The native runtime catches Ruby exceptions before they cross the C ABI. This exception contains copied
/// diagnostic text and never exposes a Ruby object, callback, or native allocation.
/// </remarks>
public sealed class LinguistRubyException : LinguistException
{
    /// <summary>Initializes a new instance of the <see cref="LinguistRubyException" /> class.</summary>
    /// <param name="message">The Ruby exception message.</param>
    /// <param name="rubyClass">The Ruby exception class name, when supplied by the native runtime.</param>
    /// <param name="rubyBacktrace">The Ruby backtrace, when supplied by the native runtime.</param>
    /// <example>
    /// <code>throw new LinguistRubyException("undefined method", "NoMethodError", "bridge.rb:12");</code>
    /// </example>
    public LinguistRubyException(string message, string? rubyClass, string? rubyBacktrace)
        : base(message)
    {
        RubyClass = rubyClass;
        RubyBacktrace = rubyBacktrace;
    }

    /// <summary>Gets the copied Ruby exception class name, or <see langword="null" /> when unavailable.</summary>
    public string? RubyClass { get; }

    /// <summary>Gets the copied Ruby backtrace, or <see langword="null" /> when unavailable.</summary>
    public string? RubyBacktrace { get; }
}
