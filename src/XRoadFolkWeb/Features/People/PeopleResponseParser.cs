using System.Xml.Linq;
using System.Xml;
using System.Diagnostics.CodeAnalysis;

namespace XRoadFolkWeb.Features.People
{
    public sealed partial class PeopleResponseParser(ILogger<PeopleResponseParser> logger)
    {
        private readonly ILogger<PeopleResponseParser> _logger = logger;
        private const int MaxElementDepth = 128; // reasonable anti-recursion cap

        // Wrapper that enforces a maximum element depth while delegating to the inner reader
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
            public override XmlNameTable? NameTable => _inner.NameTable;
            public override XmlNodeType NodeType => _inner.NodeType;
            public override string Prefix => _inner.Prefix;
            public override char QuoteChar => _inner.QuoteChar;
            public override ReadState ReadState => _inner.ReadState;
            public override string Value => _inner.Value;
            public override string? XmlLang => _inner.XmlLang;
            public override XmlSpace XmlSpace => _inner.XmlSpace;

            public override void Close() => _inner.Close();

            protected override void Dispose(bool disposing)
            {
                if (disposing) { _inner.Dispose(); }
                base.Dispose(disposing);
            }

            public override string? GetAttribute(int i) => _inner.GetAttribute(i);
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

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of XmlReader is transferred to DepthLimitingXmlReader which disposes it; CloseInput ensures StringReader is disposed when reader is disposed.")]
        private static DepthLimitingXmlReader CreateSafeReader(string xml)
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = 10 * 1024 * 1024, // 10 MB cap to avoid memory DoS
                CloseInput = true
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

                List<XElement> people = [.. doc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo")];

                string? requestSsn = doc
                    .Descendants().FirstOrDefault(e => e.Name.LocalName == "ListOfPersonPublicInfoCriteria")?
                    .Descendants().FirstOrDefault(e => e.Name.LocalName == "PersonPublicInfoCriteria")?
                    .Elements().FirstOrDefault(e => e.Name.LocalName == "SSN")?
                    .Value?.Trim();

                foreach (XElement? p in people)
                {
                    string? publicId = p.Elements().FirstOrDefault(x => x.Name.LocalName == "PublicId")?.Value?.Trim()
                                    ?? p.Elements().FirstOrDefault(x => x.Name.LocalName == "PersonId")?.Value?.Trim();

                    IEnumerable<XElement> nameItems = p.Elements().FirstOrDefault(x => x.Name.LocalName == "Names")?
                                   .Elements().Where(x => x.Name.LocalName == "Name")
                                ?? [];

                    List<string?> firstNames = [.. nameItems
                        .Where(n => string.Equals(
                            n.Elements().FirstOrDefault(e => e.Name.LocalName == "Type")?.Value,
                            "FirstName", StringComparison.OrdinalIgnoreCase))
                        .Select(n => new
                        {
                            OrderText = n.Elements().FirstOrDefault(e => e.Name.LocalName == "Order")?.Value,
                            Value = n.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value?.Trim()
                        })
                        .OrderBy(n => int.TryParse(n.OrderText, out int o) ? o : int.MaxValue)
                        .Select(n => n.Value)
                        .Where(v => !string.IsNullOrWhiteSpace(v))];

                    string? firstName = firstNames.Count > 0 ? string.Join(" ", firstNames) : null;

                    string? lastName = nameItems
                        .Where(n => string.Equals(
                            n.Elements().FirstOrDefault(e => e.Name.LocalName == "Type")?.Value,
                            "LastName", StringComparison.OrdinalIgnoreCase))
                        .Select(n => n.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value?.Trim())
                        .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

                    string? civilStatusDate = p.Elements().FirstOrDefault(x => x.Name.LocalName == "CivilStatusDate")?.Value?.Trim();
                    string? dateOfBirth = !string.IsNullOrWhiteSpace(civilStatusDate) && civilStatusDate.Length >= 10
                        ? civilStatusDate[..10]
                        : civilStatusDate;

                    string? ssn = p.Elements().FirstOrDefault(x => x.Name.LocalName == "SSN")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(ssn) && people.Count == 1 && !string.IsNullOrWhiteSpace(requestSsn))
                    {
                        ssn = requestSsn;
                    }

                    bool hasIdentifier = !string.IsNullOrWhiteSpace(publicId) || !string.IsNullOrWhiteSpace(ssn);
                    bool hasAnyName = !string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(lastName);
                    if (!hasIdentifier && !hasAnyName)
                    {
                        continue;
                    }

                    rows.Add(new PersonRow
                    {
                        PublicId = publicId,
                        SSN = ssn,
                        FirstName = firstName,
                        LastName = lastName,
                        DateOfBirth = dateOfBirth
                    });
                }
            }
            catch (Exception ex)
            {
                LogParsePeopleListFailed(_logger, ex, xml?.Length ?? 0);
                // malformed/unsafe XML -> return empty list
            }
            return rows;
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
                XElement? body = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Body");
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
                    List<XElement> children = [.. el.Elements()];
                    if (children.Count == 0)
                    {
                        string? v = el.Value?.Trim();
                        if (!string.IsNullOrEmpty(v))
                        {
                            string key = string.IsNullOrEmpty(path) ? el.Name.LocalName : path;
                            pairs.Add((key, v));
                        }
                        return;
                    }

                    foreach (IGrouping<string, XElement> grp in children.GroupBy(c => c.Name.LocalName))
                    {
                        if (grp.Count() == 1)
                        {
                            XElement child = grp.First();
                            string next = string.IsNullOrEmpty(path) ? grp.Key : $"{path}.{grp.Key}";
                            Flatten(child, next);
                        }
                        else
                        {
                            int idx = 0;
                            foreach (XElement? child in grp)
                            {
                                string next = string.IsNullOrEmpty(path) ? $"{grp.Key}[{idx}]" : $"{path}.{grp.Key}[{idx}]";
                                Flatten(child, next);
                                idx++;
                            }
                        }
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
