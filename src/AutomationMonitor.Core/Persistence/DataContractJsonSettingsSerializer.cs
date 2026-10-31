using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// The shipped <see cref="ISettingsSerializer"/>: the framework's
    /// <see cref="DataContractJsonSerializer"/> over the explicit data-contract documents.
    ///
    /// <para>
    /// Why this engine: it is part of the <c>netstandard2.0</c> surface, so the monitoring core keeps
    /// zero NuGet dependencies and the distribution payload stays the two approved assemblies. The
    /// emitted core references only <c>netstandard</c>, and the .NET Framework 4.8 facade forwards the
    /// type into <c>System.Runtime.Serialization</c>, which XrmToolBox already has. That is compile-time
    /// and metadata evidence: the runtime binding under .NET Framework is proved on Windows, not here.
    /// </para>
    ///
    /// <para>
    /// Two behaviours of the engine are compensated for here rather than in the file format:
    /// timestamps are strings (its own <see cref="DateTimeOffset"/> form is a legacy
    /// <c>/Date(ms)/</c> object), and the object-graph quota is raised, because the default is smaller
    /// than one watchlist of a few hundred members.
    /// </para>
    /// </summary>
    public sealed class DataContractJsonSettingsSerializer : ISettingsSerializer
    {
        /// <summary>Enough items for a large watchlist with room for the metadata around it.</summary>
        private const int MaximumGraphItems = 262144;

        /// <summary>Ceiling on what one settings file may be read as. A bigger file is refused, not truncated.</summary>
        private const int MaximumBytesToRead = 16 * 1024 * 1024;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public string EngineName
        {
            get { return "DataContractJsonSerializer"; }
        }

        public string Serialize(SettingsDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            try
            {
                using (MemoryStream buffer = new MemoryStream())
                {
                    CreateSerializer().WriteObject(buffer, document);
                    return Utf8NoBom.GetString(buffer.ToArray());
                }
            }
            catch (Exception error)
            {
                throw Translate(error, "could not be written");
            }
        }

        public SettingsDocument Deserialize(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new SettingsSerializationException("The settings file is empty.");
            }

            // A JSON object that never closes is not a settings file, whatever the reader says:
            // DataContractJsonSerializer stops at the end of the top-level object and reports success,
            // so a payload truncated mid-write - or a foreign file that merely begins with a brace -
            // comes back as a document whose collections are quietly empty. That reads as "no
            // watchlists", and the next save would then overwrite what the operator really had.
            // Judge the shape of the payload before its type.
            string payload = text.Trim();
            if (payload[0] != '{' || payload[payload.Length - 1] != '}')
            {
                throw new SettingsSerializationException(
                    payload[0] != '{'
                        ? "The file does not begin with a JSON object, so it is not a settings file written by this plugin."
                        : "The settings file ends inside a JSON object: it is a partial write, so it was not read.");
            }

            byte[] bytes = Utf8NoBom.GetBytes(text);
            if (bytes.Length > MaximumBytesToRead)
            {
                throw new SettingsSerializationException(
                    "The settings file is larger than " + MaximumBytesToRead.ToString(CultureInfo.InvariantCulture)
                    + " bytes and was not read.");
            }

            try
            {
                using (MemoryStream buffer = new MemoryStream(bytes, 0, bytes.Length, false))
                {
                    object? root = CreateSerializer().ReadObject(buffer);
                    SettingsDocument? document = root as SettingsDocument;
                    if (document == null)
                    {
                        throw new SettingsSerializationException("The file does not contain a settings document.");
                    }

                    return document;
                }
            }
            catch (SettingsSerializationException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw Translate(error, "could not be read");
            }
        }

        /// <summary>
        /// A fresh serializer per operation. Building one is trivial next to a file read, and this
        /// removes any question of two views saving settings at the same moment interfering.
        /// </summary>
        private static DataContractJsonSerializer CreateSerializer()
        {
            DataContractJsonSerializerSettings settings = new DataContractJsonSerializerSettings();

            // The engine's default graph quota is smaller than one watchlist of a few hundred
            // members, which would make an ordinary save fail. The read-side byte ceiling is enforced
            // by this class instead, because the settings object has no such property on
            // netstandard2.0.
            settings.MaxItemsInObjectGraph = MaximumGraphItems;
            settings.SerializeReadOnlyTypes = false;
            settings.UseSimpleDictionaryFormat = false;
            return new DataContractJsonSerializer(typeof(SettingsDocument), settings);
        }

        /// <summary>
        /// Turn an engine failure into a <see cref="SettingsSerializationException"/> with a reason an
        /// operator can act on. The original message goes through <see cref="DiagnosticsSanitizer"/>
        /// because serializer text can echo a value read from the file, and that value may be provider
        /// text carrying a url or an identifier.
        /// </summary>
        private static SettingsSerializationException Translate(Exception error, string what)
        {
            string detail = DiagnosticsSanitizer.Sanitize(error.Message);
            string reason;
            if (error is SerializationException
                || error is FormatException
                || error is XmlException
                || error is InvalidDataContractException
                || error is EndOfStreamException
                || error is ArgumentException)
            {
                reason = "The local settings file " + what + ": " + detail;
            }
            else
            {
                reason = DiagnosticsSanitizer.Describe(
                    "The local settings file " + what + " after an unexpected " + error.GetType().Name + ".",
                    detail);
            }

            return new SettingsSerializationException(reason, error);
        }
    }
}
