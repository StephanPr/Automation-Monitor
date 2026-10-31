using System;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// The persistence contract of the local settings store: turn a settings document into text and
    /// back. Everything above it - schema gate, atomic write, backup, recovery, watchlist management
    /// - is written against this interface, so replacing the JSON engine later is one new class and
    /// no change to watchlist logic.
    ///
    /// <para>
    /// The shipped implementation is the framework's
    /// <c>System.Runtime.Serialization.Json.DataContractJsonSerializer</c>. It is deliberately the
    /// only place that type is named: it needs no NuGet package on <c>netstandard2.0</c>, which keeps
    /// the distribution payload at the two assemblies the package validator allows.
    /// </para>
    /// </summary>
    public interface ISettingsSerializer
    {
        /// <summary>Stable name of the engine, recorded so a file's origin can be explained later.</summary>
        string EngineName { get; }

        /// <summary>Portable text for a document. Throws <see cref="SettingsSerializationException"/> when the document cannot be represented.</summary>
        string Serialize(SettingsDocument document);

        /// <summary>
        /// Read text back. Any structural problem, unsupported type or malformed value is reported as
        /// <see cref="SettingsSerializationException"/> with a sanitized reason: the caller decides
        /// whether that means "first run", "corrupt" or "newer than this build".
        /// </summary>
        SettingsDocument Deserialize(string text);
    }
}
