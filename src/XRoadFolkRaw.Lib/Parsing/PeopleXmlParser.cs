using System.Xml;
using System.Xml.Linq;
using System.IO;

namespace XRoadFolkRaw.Lib
{
    public static class PeopleXmlParser
    {
        private static XmlReader CreateSafeReader(string xml)
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = 10 * 1024 * 1024
            };
            return XmlReader.Create(new StringReader(xml), settings);
        }

        public static string FormatPretty(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return xml;
            try
            {
                using XmlReader reader = CreateSafeReader(xml);
                XDocument doc = XDocument.Load(reader, LoadOptions.None);
                return doc.ToString(SaveOptions.None);
            }
            catch
            {
                return xml;
            }
        }

        public static List<PersonRow> ParsePeopleList(string xml)
        {
            List<PersonRow> rows = [];
            if (string.IsNullOrWhiteSpace(xml)) return rows;

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

                foreach (XElement p in people)
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
            catch
            {
                // return empty rows on malformed/unsafe XML
            }
            return rows;
        }

        public static List<(string Key, string Value)> FlattenResponse(string xml)
        {
            List<(string, string)> pairs = [];
            if (string.IsNullOrWhiteSpace(xml)) return pairs;

            XDocument doc = XDocument.Parse(xml);
            XElement? body = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Body");
            if (body is null) return pairs;

            XElement? resp = body.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Response", StringComparison.OrdinalIgnoreCase));
            if (resp is null) return pairs;

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
                        foreach (XElement child in grp)
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
            return pairs;
        }
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
