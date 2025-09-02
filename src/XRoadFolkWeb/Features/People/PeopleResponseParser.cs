using System.Diagnostics.CodeAnalysis;
using System.Xml;
using System.Xml.Linq;

namespace XRoadFolkWeb.Features.People
{
    public sealed partial class PeopleResponseParser(ILogger<PeopleResponseParser> logger)
    {
        private readonly ILogger<PeopleResponseParser> _logger = logger;
        private const int MaxElementDepth = 128; // reasonable anti-recursion cap

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

            public override string GetAttribute(int i)
            {
                return _inner.GetAttribute(i);
            }

            public override string? GetAttribute(string name)
            {
                return _inner.GetAttribute(name);
            }

            public override string? GetAttribute(string name, string? namespaceURI)
            {
                return _inner.GetAttribute(name, namespaceURI);
            }

            public override string? LookupNamespace(string prefix)
            {
                return _inner.LookupNamespace(prefix);
            }

            public override bool MoveToAttribute(string name)
            {
                return _inner.MoveToAttribute(name);
            }

            public override bool MoveToAttribute(string name, string? ns)
            {
                return _inner.MoveToAttribute(name, ns);
            }

            public override bool MoveToElement()
            {
                return _inner.MoveToElement();
            }

            public override bool MoveToFirstAttribute()
            {
                return _inner.MoveToFirstAttribute();
            }

            public override bool MoveToNextAttribute()
            {
                return _inner.MoveToNextAttribute();
            }

            public override bool ReadAttributeValue()
            {
                return _inner.ReadAttributeValue();
            }

            public override void ResolveEntity()
            {
                _inner.ResolveEntity();
            }

            public override string ReadInnerXml()
            {
                return _inner.ReadInnerXml();
            }

            public override string ReadOuterXml()
            {
                return _inner.ReadOuterXml();
            }

            public override XmlReader ReadSubtree()
            {
                return new DepthLimitingXmlReader(_inner.ReadSubtree(), _maxDepth);
            }
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of XmlReader is transferred to DepthLimitingXmlReader which disposes it; CloseInput ensures StringReader is disposed when reader is disposed.")]
        private static DepthLimitingXmlReader CreateSafeReader(string xml)
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = 10 * 1024 * 1024, // 10 MB cap to avoid memory DoS
                CloseInput = true,
            };

            StringReader sr = new(xml);
            XmlReader? inner = null;
            try
            {
                inner = XmlReader.Create(sr, settings);
                return new DepthLimitingXmlReader(inner, MaxElementDepth);
            }
            catch
            {
                inner?.Dispose();
                sr.Dispose();
                throw;
            }
        }

        public List<PersonRow> ParsePeopleList(string xml)
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
                    string? ssn = ExtractSsn(p, people.Count, requestSsn);

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
            return [.. doc.Descendants().Where(e => string.Equals(e.Name.LocalName, "PersonPublicInfo", StringComparison.Ordinal))];
        }

        private static string? ExtractRequestSsn(XDocument doc)
        {
            return doc
                .Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "ListOfPersonPublicInfoCriteria", StringComparison.Ordinal))?
                .Descendants().FirstOrDefault(static e => string.Equals(e.Name.LocalName, "PersonPublicInfoCriteria", StringComparison.Ordinal))?
                .Elements().FirstOrDefault(static e => string.Equals(e.Name.LocalName, "SSN", StringComparison.Ordinal))?
                .Value?.Trim();
        }

        private static string? ExtractPublicId(XElement p)
        {
            return p.Elements().FirstOrDefault(static x => string.Equals(x.Name.LocalName, "PublicId", StringComparison.Ordinal))?.Value?.Trim()
                ?? p.Elements().FirstOrDefault(x => string.Equals(x.Name.LocalName, "PersonId", StringComparison.Ordinal))?.Value?.Trim();
        }

        private static IEnumerable<XElement> GetNameItems(XElement p)
        {
            return p.Elements().FirstOrDefault(x => string.Equals(x.Name.LocalName, "Names", StringComparison.Ordinal))?
                       .Elements().Where(x => string.Equals(x.Name.LocalName, "Name", StringComparison.Ordinal))
                   ?? [];
        }

        private static string? ExtractFirstName(IEnumerable<XElement> nameItems)
        {
            List<string?> firstNames = [.. nameItems
                .Where(n => string.Equals(
                    n.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "Type", StringComparison.Ordinal))?.Value,
                    "FirstName", StringComparison.OrdinalIgnoreCase))
                .Select(n => new
                {
                    OrderText = n.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "Order", StringComparison.Ordinal))?.Value,
                    Value = n.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "Value", StringComparison.Ordinal))?.Value?.Trim(),
                })
                .OrderBy(static n => int.TryParse(n.OrderText, out int o) ? o : int.MaxValue)
                .Select(n => n.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v)),];

            return firstNames.Count > 0 ? string.Join(' ', firstNames) : null;
        }

        private static string? ExtractLastName(IEnumerable<XElement> nameItems)
        {
            return nameItems
                .Where(n => string.Equals(
                    n.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "Type", StringComparison.Ordinal))?.Value,
                    "LastName", StringComparison.OrdinalIgnoreCase))
                .Select(n => n.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "Value", StringComparison.Ordinal))?.Value?.Trim())
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        private static string? ExtractDateOfBirth(XElement p)
        {
            string? civilStatusDate = p.Elements().FirstOrDefault(x => string.Equals(x.Name.LocalName, "CivilStatusDate", StringComparison.Ordinal))?.Value?.Trim();
            return !string.IsNullOrWhiteSpace(civilStatusDate) && civilStatusDate.Length >= 10
                ? civilStatusDate[..10]
                : civilStatusDate;
        }

        private static string? ExtractSsn(XElement p, int peopleCount, string? requestSsn)
        {
            string? ssn = p.Elements().FirstOrDefault(x => string.Equals(x.Name.LocalName, "SSN", StringComparison.Ordinal))?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(ssn) && peopleCount == 1 && !string.IsNullOrWhiteSpace(requestSsn))
            {
                ssn = requestSsn;
            }
            return ssn;
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

        public List<(string Key, string Value)> FlattenResponse(string xml)
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
                XElement? body = doc.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "Body", StringComparison.Ordinal));
                if (body == null)
                {
                    return pairs;
                }

                XElement? resp = body.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Response", StringComparison.OrdinalIgnoreCase));
                if (resp == null)
                {
                    return pairs;
                }

                void Flatten(XElement el, string path)
                {
                    // Fast path: no child elements -> emit leaf value if non-empty
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

                    // Count occurrences of each child name (avoid List + GroupBy allocations)
                    Dictionary<string, int> counts = new(StringComparer.Ordinal);
                    foreach (XElement c in el.Elements())
                    {
                        string name = c.Name.LocalName;
                        counts[name] = counts.TryGetValue(name, out int cnt) ? cnt + 1 : 1;
                    }

                    // Emit children with optional index for duplicates
                    Dictionary<string, int>? indexes = null;
                    foreach (XElement c in el.Elements())
                    {
                        string name = c.Name.LocalName;
                        int count = counts[name];
                        string next;
                        if (count == 1)
                        {
                            next = string.IsNullOrEmpty(path) ? name : path + "." + name;
                        }
                        else
                        {
                            indexes ??= new Dictionary<string, int>(StringComparer.Ordinal);
                            int idx = indexes.TryGetValue(name, out int cur) ? cur : 0;
                            indexes[name] = idx + 1;
                            next = string.IsNullOrEmpty(path) ? $"{name}[{idx}]" : $"{path}.{name}[{idx}]";
                        }
                        Flatten(c, next);
                    }
                }

                foreach (XElement child in resp.Elements())
                {
                    Flatten(child, "");
                }
            }
            catch (Exception ex)
            {
                LogFlattenResponseFailed(_logger, ex, xml?.Length ?? 0);
                // malformed/unsafe XML -> return empty
            }
            return pairs;
        }

        public string PrettyFormatXml(string xml)
        {
            try
            {
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

    public sealed class PersonRow
    {
        public string? PublicId { get; set; }
        public string? SSN { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? DateOfBirth { get; set; }
    }
}
