using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Ahlena.Modules.Cms.Application.Landing;

/// <summary>
/// Sanitizes an uploaded SVG before it is stored.
/// <para>
/// The input is accepted only when it is a well-formed XML document whose root
/// element is <c>&lt;svg&gt;</c>, parsed with DTD processing prohibited and no
/// external resolver. A document that declares a DTD (the only way to declare
/// entities) therefore fails to parse and is rejected, so XXE and entity
/// expansion are impossible rather than merely filtered. The document must also
/// nest no deeper than <see cref="MaxNestingDepth"/>: that bound is checked by a
/// streaming pass that builds no tree and recurses nowhere, before the DOM
/// loader, the sanitize pass and the serializer — all of which walk depth —
/// ever see the document. Anything that cannot be parsed safely is rejected,
/// and the raw bytes are never persisted.
/// </para>
/// <para>
/// Active content is then removed from the parsed tree: <c>script</c>,
/// <c>foreignObject</c>, <c>style</c>, <c>iframe</c>, <c>embed</c>,
/// <c>object</c> and the SMIL animation elements; XML comments and processing
/// instructions; every event-handler (<c>on*</c>) attribute; every external
/// reference (<c>href</c>/<c>xlink:href</c>/<c>src</c> that is not a
/// same-document <c>#fragment</c>); and any retained attribute carrying a CSS
/// <c>url(...)</c> that is not a same-document <c>url(#…)</c>. The returned
/// bytes are the sanitized serialization, and the caller stores those.
/// </para>
/// </summary>
public static class LandingSvgSanitizer
{
    // BMP-sized ceiling for the markup we are willing to parse (~5 MiB). The
    // multipart endpoint caps the request at the same size.
    private const int MaxCharacters = 5 * 1024 * 1024;

    /// <summary>
    /// Maximum element nesting accepted, root included. A deeper document is
    /// rejected before a tree is built: .NET cannot catch a
    /// <see cref="StackOverflowException"/>, so recursing into a pathologically
    /// nested document would terminate the process instead of failing the
    /// upload.
    /// </summary>
    public const int MaxNestingDepth = 256;

    private const string UrlFunction = "url(";

    private const string XLinkHrefLocalName = "href";

    private static readonly HashSet<string> ForbiddenElements =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "script",
            "foreignobject",
            "style",
            "iframe",
            "embed",
            "object",

            // SMIL animation retargets attributes and references at runtime
            // (for example <set attributeName="xlink:href" to="..."/>); a static
            // illustration needs none of it.
            "set",
            "animate",
            "animatetransform",
            "animatemotion",
            "discard"
        };

    /// <summary>
    /// Parses, sanitizes and serializes <paramref name="input"/>. Returns
    /// <c>false</c> (and no output) when the payload cannot be parsed safely, is
    /// nested deeper than <see cref="MaxNestingDepth"/>, or is not an SVG
    /// document.
    /// </summary>
    public static bool TrySanitize(
        ReadOnlySpan<byte> input,
        out byte[] sanitized)
    {
        sanitized = [];

        if (input.IsEmpty)
            return false;

        try
        {
            // Depth first, with a streaming pass that builds no tree and
            // recurses nowhere: a deeply nested document is rejected before the
            // DOM loader, the sanitize pass and the serializer walk it.
            if (!HasBoundedNestingDepth(input))
                return false;

            XDocument document;

            using (var stream = new MemoryStream(input.ToArray(), writable: false))
            using (XmlReader reader = XmlReader.Create(stream, CreateReaderSettings()))
            {
                document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            }

            XElement? root = document.Root;
            if (root is null ||
                !string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Depth is already <= MaxNestingDepth, so this recursion — and the
            // serialization below — stay within a bounded number of frames.
            SanitizeElement(root);

            var writerSettings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = false,
                OmitXmlDeclaration = false
            };

            using var output = new MemoryStream();
            using (XmlWriter writer = XmlWriter.Create(output, writerSettings))
            {
                document.Save(writer);
            }

            sanitized = output.ToArray();
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static XmlReaderSettings CreateReaderSettings() =>
        new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = MaxCharacters,
            MaxCharactersInDocument = MaxCharacters,
            ConformanceLevel = ConformanceLevel.Document
        };

    /// <summary>
    /// Iterative well-formedness and depth check. The pull reader keeps its own
    /// element state, so nothing here recurses, and the pass returns at the
    /// first element deeper than <see cref="MaxNestingDepth"/> instead of
    /// reading the rest of the payload.
    /// </summary>
    private static bool HasBoundedNestingDepth(ReadOnlySpan<byte> input)
    {
        int depth = 0;

        using var stream = new MemoryStream(input.ToArray(), writable: false);
        using XmlReader reader = XmlReader.Create(stream, CreateReaderSettings());

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when !reader.IsEmptyElement:
                    if (++depth > MaxNestingDepth)
                        return false;
                    break;

                case XmlNodeType.EndElement:
                    depth--;
                    break;
            }
        }

        return true;
    }

    private static void SanitizeElement(XElement element)
    {
        RemoveUnsafeAttributes(element);

        // Snapshot the children: the tree is mutated while it is walked.
        foreach (XNode child in element.Nodes().ToArray())
        {
            switch (child)
            {
                // Comments and processing instructions render nothing, and a
                // processing instruction can load an external stylesheet; an
                // illustration needs neither.
                case XComment:
                case XProcessingInstruction:
                    child.Remove();
                    break;

                case XElement childElement:
                    if (ForbiddenElements.Contains(childElement.Name.LocalName))
                    {
                        childElement.Remove();
                        continue;
                    }

                    SanitizeElement(childElement);
                    break;
            }
        }
    }

    private static void RemoveUnsafeAttributes(XElement element)
    {
        foreach (XAttribute attribute in element.Attributes().ToArray())
        {
            // Event handlers: onclick, onload, onbegin, onmouseover, ...
            if (attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            {
                attribute.Remove();
                continue;
            }

            // External references: only same-document fragments survive.
            if (IsReferenceAttribute(attribute) && !IsLocalFragment(attribute.Value))
            {
                attribute.Remove();
                continue;
            }

            // Any other url(...) — in a style or presentation attribute — is an
            // egress/tracking channel unless it targets a same-document
            // fragment.
            if (HasNonLocalUrlReference(attribute.Value))
            {
                attribute.Remove();
            }
        }
    }

    private static bool IsReferenceAttribute(XAttribute attribute) =>
        string.Equals(attribute.Name.LocalName, XLinkHrefLocalName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(attribute.Name.LocalName, "src", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalFragment(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        return trimmed.StartsWith('#') && trimmed.Length > 1;
    }

    /// <summary>
    /// True when the value contains a CSS <c>url(...)</c> whose target is not a
    /// same-document <c>#fragment</c>. A <c>url(</c> with no closing parenthesis
    /// is treated as unsafe.
    /// </summary>
    private static bool HasNonLocalUrlReference(string? value)
    {
        string text = value ?? string.Empty;
        int index = 0;

        while (true)
        {
            int start = text.IndexOf(UrlFunction, index, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return false;

            int open = start + UrlFunction.Length;
            int close = text.IndexOf(')', open);
            if (close < 0)
                return true;

            string target = text[open..close].Trim().Trim('\'', '"').Trim();
            if (!target.StartsWith('#'))
                return true;

            index = close + 1;
        }
    }
}
