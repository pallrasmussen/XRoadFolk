using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Features.People;

namespace XRoadFolkWeb.Features.Index
{
    public sealed class PersonDetailsProvider(PeopleService service, PeopleResponseParser parser, IConfiguration config)
    {
        private readonly PeopleService _service = service;
        private readonly PeopleResponseParser _parser = parser;
        private readonly IConfiguration _config = config;

        public async Task<(List<(string Key, string Value)> Details, string Pretty, string SelectedNameSuffix)> GetAsync(
            string publicId,
            Microsoft.Extensions.Localization.IStringLocalizer loc,
            IReadOnlyCollection<string>? allowedIncludeKeys = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(loc);

            string xml = await _service.GetPersonAsync(publicId, ct).ConfigureAwait(false);
            List<(string Key, string Value)> pairs = _parser.FlattenResponse(xml);

            // Replace the two consecutive .Where() calls with a single .Where() that combines both predicates

            List<(string Key, string Value)> filtered = [.. pairs
                .Where(p =>
                {
                    // First predicate: filter out request header/body and noisy IDs
                    if (string.IsNullOrEmpty(p.Key))
                    {
                        return true;
                    }

                    string k = p.Key;
                    bool isHeaderOrBody = k.StartsWith("requestheader", StringComparison.OrdinalIgnoreCase)
                        || k.StartsWith("requestbody", StringComparison.OrdinalIgnoreCase)
                        || k.Contains(".requestheader", StringComparison.OrdinalIgnoreCase)
                        || k.Contains(".requestbody", StringComparison.OrdinalIgnoreCase);

                    if (isHeaderOrBody)
                    {
                        return false;
                    }

                    // Second predicate: filter out certain keys
                    string key = p.Key;
                    int lastDot = key.LastIndexOf('.');
                    string sub = lastDot >= 0 ? key[(lastDot + 1)..] : key;
                    int bpos = sub.IndexOf('[', StringComparison.Ordinal);
                    if (bpos >= 0)
                    {
                        sub = sub[..bpos];
                    }

                    string s = sub.ToLowerInvariant();
                    return s is not "id" and not "fixed" and not "authoritycode" and not "personaddressid";
                }),
            ];

            // Include allow-list filter (use provided keys if present, else from configuration)
            HashSet<string> allowed = (allowedIncludeKeys?.Count > 0)
                ? new HashSet<string>(allowedIncludeKeys, StringComparer.OrdinalIgnoreCase)
                : IncludeConfigHelper.GetEnabledIncludeKeys(_config);

            // Keep core sections allowed, but no placeholders will be added; only actual data will render
            _ = allowed.Add("Person");
            _ = allowed.Add("Names");

            if (allowed.Count > 0)
            {
                static bool Matches(string seg, string allowedKey)
                {
                    return seg.Equals(allowedKey, StringComparison.OrdinalIgnoreCase)
                        || seg.StartsWith(allowedKey, StringComparison.OrdinalIgnoreCase)
                        || allowedKey.StartsWith(seg, StringComparison.OrdinalIgnoreCase);
                }

                static IEnumerable<string> Segments(string key)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        yield break;
                    }

                    foreach (string part in key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        int i = part.IndexOf('[', StringComparison.Ordinal);
                        yield return i >= 0 ? part[..i] : part;
                    }
                }

                filtered = [.. filtered.Where(p =>
                {
                    if (string.IsNullOrWhiteSpace(p.Key))
                    {
                        return false;
                    }

                    foreach (string seg in Segments(p.Key))
                    {
                        foreach (string a in allowed)
                        {
                            if (Matches(seg, a))
                            {
                                return true;
                            }
                        }
                    }
                    return false;
                }),];
            }

            string? first = filtered.Find(p => p.Key.EndsWith(".FirstName", StringComparison.OrdinalIgnoreCase)).Value;
            string? last = filtered.Find(p => p.Key.EndsWith(".LastName", StringComparison.OrdinalIgnoreCase)).Value;
            string selectedNameSuffix = (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
                ? loc["SelectedNameSuffixFormat", string.Join(' ', new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)))]
                : string.Empty;

            return (filtered, _parser.PrettyFormatXml(xml), selectedNameSuffix);
        }
    }
}
