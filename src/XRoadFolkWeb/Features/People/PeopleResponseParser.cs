using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace XRoadFolkWeb.Features.People
{
    public sealed partial class PeopleResponseParser(ILogger<PeopleResponseParser> logger)
    {
        private readonly ILogger<PeopleResponseParser> _logger = logger;
        private const int MaxElementDepth = 128; // reasonable anti-recursion cap

        // Helpers for namespace-agnostic matches
        private static bool IsName(XElement e, string local) => string.Equals(e.Name.LocalName, local, StringComparison.Ordinal);
        private static IEnumerable<XElement> ElementsBy(XElement e, string local) => e.Elements().Where(x => IsName(x, local));

        /// <summary>
        /// Wrapper that enforces a maximum element depth while delegating to the inner reader
        /// </summary>
        private sealed class DepthLimitingXmlReader(XmlReader inner, int maxDepth) : XmlReader
        {
            private readonly XmlReader _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            private readonly int _maxDepth = Math.Max(1, maxDepth);

            private void CheckDepth()
            {
                if (_inner.Depth > _maxDepth)
                {
                    throw new XmlException($"XML maximum depth {_maxDepth} exceeded");
                }
            }

            public override bool Read()
            {
                bool res = _inner.Read();
                if (res) { CheckDepth(); }
                return res;
            }

            public override int AttributeCount => _inner.AttributeCount;
            public override string BaseURI => _inner.BaseURI;
            public override int Depth => _inner.Depth;
            public override bool EOF => _inner.EOF;
            public override bool HasValue => _inner.HasValue;
            public override bool IsDefault => _inner.IsDefault;
            public override bool IsEmptyElement => _inner.IsEmptyElement;
            public override string LocalName => _inner.LocalName;
            public override string Name => _inner.Name;
            public override string NamespaceURI => _inner.NamespaceURI;
            public override XmlNameTable NameTable => _inner.NameTable!;
            public override XmlNodeType NodeType => _inner.NodeType;
            public override string Prefix => _inner.Prefix;
            public override char QuoteChar => _inner.QuoteChar;
            public override ReadState ReadState => _inner.ReadState;
            public override string Value => _inner.Value;
            public override string XmlLang => _inner.XmlLang!;
            public override XmlSpace XmlSpace => _inner.XmlSpace;

            public override void Close()
            {
                _inner.Close();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { _inner.Dispose(); }
                base.Dispose(disposing);
            }

            public override string GetAttribute(int i) => _inner.GetAttribute(i);
            public override string? GetAttribute(string name) => _inner.GetAttribute(name);
            public override string? GetAttribute(string name, string? namespaceURI) => _inner.GetAttribute(name, namespaceURI);
            public override string? LookupNamespace(string prefix) => _inner.LookupNamespace(prefix);
            public override bool MoveToAttribute(string name) => _inner.MoveToAttribute(name);
            public override bool MoveToAttribute(string name, string? ns) => _inner.MoveToAttribute(name, ns);
            public override bool MoveToElement() => _inner.MoveToElement();
            public override bool MoveToFirstAttribute() => _inner.MoveToFirstAttribute();
            public override bool MoveToNextAttribute() => _inner.MoveToNextAttribute();
            public override bool ReadAttributeValue() => _inner.ReadAttributeValue();
            public override void ResolveEntity() => _inner.ResolveEntity();
            public override string ReadInnerXml() => _inner.ReadInnerXml();
            public override string ReadOuterXml() => _inner.ReadOuterXml();
            public override XmlReader ReadSubtree() => new DepthLimitingXmlReader(_inner.ReadSubtree(), _maxDepth);
        }

        private static DepthLimitingXmlReader CreateSafeReader(string xml)
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = 10 * 1024 * 1024, // 10 MB cap to avoid memory DoS
                CloseInput = true, // dispose underlying StringReader when reader is disposed
            };

            // CA2000: Ownership of both XmlReader and StringReader is transferred to the returned DepthLimitingXmlReader,
            // which disposes the inner reader; CloseInput=true disposes the StringReader as well.
#pragma warning disable CA2000
            var stringReader = new StringReader(xml);
            var inner = XmlReader.Create(stringReader, settings);
#pragma warning restore CA2000
            return new DepthLimitingXmlReader(inner, MaxElementDepth);
        }

        public IReadOnlyList<PersonRow> ParsePeopleList(string? xml)
        {
            List<PersonRow> rows = [];
            if (string.IsNullOrWhiteSpace(xml))
            {
                return rows;
            }

            try
            {
                using XmlReader reader = CreateSafeReader(xml);
                XDocument doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);

                List<XElement> people = FindPersonElements(doc);
                string? requestSsn = ExtractRequestSsn(doc);

                foreach (XElement p in people)
                {
                    string? publicId = ExtractPublicId(p);
                    IEnumerable<XElement> nameItems = GetNameItems(p);
                    string? firstName = ExtractFirstName(nameItems);
                    string? lastName = ExtractLastName(nameItems);
                    string? dateOfBirth = ExtractDateOfBirth(p);
                    string? ssn = ExtractSsn(p);

                    // Only fallback to request SSN when a single result exists AND it has other identifying data
                    if (string.IsNullOrWhiteSpace(ssn)
                        && people.Count == 1
                        && !string.IsNullOrWhiteSpace(requestSsn)
                        && (!string.IsNullOrWhiteSpace(publicId) || !string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(lastName)))
                    {
                        ssn = requestSsn;
                    }

                    if (!ShouldIncludeRow(publicId, ssn, firstName, lastName))
                    {
                        continue;
                    }

                    rows.Add(BuildRow(publicId, ssn, firstName, lastName, dateOfBirth));
                }
            }
            catch (Exception ex)
            {
                LogParsePeopleListFailed(_logger, ex, xml?.Length ?? 0);
                // malformed/unsafe XML -> return empty list
            }
            return rows;
        }

        private static List<XElement> FindPersonElements(XDocument doc)
        {
            return [.. doc.Descendants().Where(e => IsName(e, "PersonPublicInfo"))];
        }

        private static string? ExtractRequestSsn(XDocument doc)
        {
            return doc
                .Descendants().FirstOrDefault(e => IsName(e, "ListOfPersonPublicInfoCriteria"))?
                .Descendants().FirstOrDefault(e => IsName(e, "PersonPublicInfoCriteria"))?
                .Elements().FirstOrDefault(e => IsName(e, "SSN"))?
                .Value?.Trim();
        }

        private static string? ExtractPublicId(XElement p)
        {
            return ElementsBy(p, "PublicId").FirstOrDefault()?.Value?.Trim()
                ?? ElementsBy(p, "PersonId").FirstOrDefault()?.Value?.Trim();
        }

        private static IEnumerable<XElement> GetNameItems(XElement p)
        {
            return ElementsBy(p, "Names").FirstOrDefault()?.Elements().Where(e => IsName(e, "Name"))
                   ?? Enumerable.Empty<XElement>();
        }

        private static string? ExtractFirstName(IEnumerable<XElement>? nameItems)
        {
            if (nameItems is null)
            {
                return null;
            }

            List<string?> firstNames = [.. nameItems
                .Where(n => string.Equals(
                    ElementsBy(n, "Type").FirstOrDefault()?.Value,
                    "FirstName", StringComparison.OrdinalIgnoreCase))
                .Select(n => new
                {
                    OrderText = ElementsBy(n, "Order").FirstOrDefault()?.Value,
                    Value = ElementsBy(n, "Value").FirstOrDefault()?.Value?.Trim(),
                })
                .OrderBy(static n => int.TryParse(n.OrderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int o) ? o : int.MaxValue)
                .Select(n => n.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v)),];

            return firstNames.Count > 0 ? string.Join(' ', firstNames) : null;
        }

        private static string? ExtractLastName(IEnumerable<XElement>? nameItems)
        {
            if (nameItems is null)
            {
                return null;
            }

            return nameItems
                .Where(n => string.Equals(
                    ElementsBy(n, "Type").FirstOrDefault()?.Value,
                    "LastName", StringComparison.OrdinalIgnoreCase))
                .Select(n => ElementsBy(n, "Value").FirstOrDefault()?.Value?.Trim())
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        private static string? ExtractDateOfBirth(XElement p)
        {
            string? civilStatusDate = ElementsBy(p, "CivilStatusDate").FirstOrDefault()?.Value?.Trim();
            return !string.IsNullOrWhiteSpace(civilStatusDate) && civilStatusDate.Length >= 10
                ? civilStatusDate[..10]
                : civilStatusDate;
        }

        private static string? ExtractSsn(XElement p)
        {
            return ElementsBy(p, "SSN").FirstOrDefault()?.Value?.Trim();
        }

        private static bool ShouldIncludeRow(string? publicId, string? ssn, string? firstName, string? lastName)
        {
            bool hasIdentifier = !string.IsNullOrWhiteSpace(publicId) || !string.IsNullOrWhiteSpace(ssn);
            bool hasAnyName = !string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(lastName);
            return hasIdentifier || hasAnyName;
        }

        private static PersonRow BuildRow(string? publicId, string? ssn, string? firstName, string? lastName, string? dateOfBirth)
        {
            return new PersonRow
            {
                PublicId = publicId,
                SSN = ssn,
                FirstName = firstName,
                LastName = lastName,
                DateOfBirth = dateOfBirth,
            };
        }

        public IReadOnlyList<(string Key, string Value)> FlattenResponse(string? xml)
        {
            List<(string, string)> pairs = [];
            if (string.IsNullOrWhiteSpace(xml))
            {
                return pairs;
            }

            try
            {
                using XmlReader reader = CreateSafeReader(xml);
                XDocument doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                XElement? body = doc.Descendants().FirstOrDefault(e => IsName(e, "Body"));
                if (body == null)
                {
                    return pairs;
                }

                XElement? resp = body.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Response", StringComparison.OrdinalIgnoreCase));
                if (resp == null)
                {
                    return pairs;
                }

                FlattenElements(resp, string.Empty, pairs);
            }
            catch (Exception ex)
            {
                LogFlattenResponseFailed(_logger, ex, xml?.Length ?? 0);
                // malformed/unsafe XML -> return empty
            }
            return pairs;
        }

        private static void FlattenElements(XElement el, string path, List<(string, string)> pairs)
        {
            // NEW: capture attributes (e.g. OfficialName on Person) as individual key/value pairs
            try
            {
                foreach (var attr in el.Attributes())
                {
                    string? aval = attr.Value?.Trim();
                    if (string.IsNullOrEmpty(aval)) { continue; }
                    string attrKeyBase = string.IsNullOrEmpty(path) ? el.Name.LocalName : path; // keep consistent path root
                    string key = string.IsNullOrEmpty(attrKeyBase)
                        ? attr.Name.LocalName
                        : attrKeyBase + "." + attr.Name.LocalName; // results in e.g. Person.OfficialName or Person[0].OfficialName
                    pairs.Add((key, aval));
                }
            }
            catch { /* ignore attribute processing issues */ }

            XElement? firstChild = el.Elements().FirstOrDefault();
            if (firstChild is null)
            {
                string? v = el.Value?.Trim();
                if (!string.IsNullOrEmpty(v))
                {
                    string key = string.IsNullOrEmpty(path) ? el.Name.LocalName : path;
                    pairs.Add((key, v));
                }
                return;
            }

            Dictionary<string, int> counts = CountChildNames(el);
            Dictionary<string, int>? indexes = null;
            foreach (XElement c in el.Elements())
            {
                string name = c.Name.LocalName;
                int count = counts[name];
                string next = ComputeNextPath(path, name, count, ref indexes);
                FlattenElements(c, next, pairs);
            }
        }

        private static Dictionary<string, int> CountChildNames(XElement el)
        {
            Dictionary<string, int> counts = new(StringComparer.Ordinal);
            foreach (XElement c in el.Elements())
            {
                string name = c.Name.LocalName;
                counts[name] = counts.TryGetValue(name, out int cnt) ? cnt + 1 : 1;
            }
            return counts;
        }

        private static string ComputeNextPath(string path, string name, int count, ref Dictionary<string, int>? indexes)
        {
            if (count == 1)
            {
                return string.IsNullOrEmpty(path) ? name : path + "." + name;
            }

            indexes ??= new Dictionary<string, int>(StringComparer.Ordinal);
            int idx = indexes.TryGetValue(name, out int cur) ? cur : 0;
            indexes[name] = idx + 1;
            return string.IsNullOrEmpty(path) ? $"{name}[{idx}]" : $"{path}.{name}[{idx}]";
        }

        public string PrettyFormatXml(string? xml)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(xml))
                {
                    return string.Empty;
                }

                using XmlReader reader = CreateSafeReader(xml);
                XDocument doc = XDocument.Load(reader, LoadOptions.None);
                return doc.ToString(SaveOptions.None);
            }
            catch (Exception ex)
            {
                LogPrettyFormatFailed(_logger, ex, xml?.Length ?? 0);
                return xml ?? string.Empty;
            }
        }

        [LoggerMessage(EventId = 5001, Level = LogLevel.Error, Message = "PeopleResponseParser: ParsePeopleList failed (xmlLength={Length})")]
        private static partial void LogParsePeopleListFailed(ILogger logger, Exception ex, int Length);

        [LoggerMessage(EventId = 5002, Level = LogLevel.Error, Message = "PeopleResponseParser: FlattenResponse failed (xmlLength={Length})")]
        private static partial void LogFlattenResponseFailed(ILogger logger, Exception ex, int Length);

        [LoggerMessage(EventId = 5003, Level = LogLevel.Warning, Message = "PeopleResponseParser: PrettyFormatXml failed (xmlLength={Length})")]
        private static partial void LogPrettyFormatFailed(ILogger logger, Exception ex, int Length);
    }
}
