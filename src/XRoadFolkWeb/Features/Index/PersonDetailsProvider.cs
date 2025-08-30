using Microsoft.Extensions.Configuration;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Features.People;

namespace XRoadFolkWeb.Features.Index
{
    public sealed class PersonDetailsProvider
    {
        private readonly PeopleService _service;
        private readonly PeopleResponseParser _parser;
        private readonly IConfiguration _config;

        public PersonDetailsProvider(PeopleService service, PeopleResponseParser parser, IConfiguration config)
        {
            _service = service;
            _parser = parser;
            _config = config;
        }

        public async Task<(List<(string Key, string Value)> Details, string Pretty, string SelectedNameSuffix)> GetAsync(string publicId, Microsoft.Extensions.Localization.IStringLocalizer loc, CancellationToken ct = default)
        {
            string xml = await _service.GetPersonAsync(publicId, ct).ConfigureAwait(false);
            List<(string Key, string Value)> pairs = _parser.FlattenResponse(xml);

            // Filter out request header/body and noisy IDs
            List<(string Key, string Value)> filtered = pairs
                .Where(p =>
                {
                    if (string.IsNullOrEmpty(p.Key)) return true;
                    string k = p.Key;
                    return !(k.StartsWith("requestheader", StringComparison.OrdinalIgnoreCase)
                          || k.StartsWith("requestbody", StringComparison.OrdinalIgnoreCase)
                          || k.Contains(".requestheader", StringComparison.OrdinalIgnoreCase)
                          || k.Contains(".requestbody", StringComparison.OrdinalIgnoreCase));
                })
                .Where(p =>
                {
                    if (string.IsNullOrEmpty(p.Key)) return true;
                    string key = p.Key;
                    int lastDot = key.LastIndexOf('.');
                    string sub = lastDot >= 0 ? key[(lastDot + 1)..] : key;
                    int bpos = sub.IndexOf('[');
                    if (bpos >= 0) sub = sub[..bpos];
                    string s = sub.ToLowerInvariant();
                    return s != "id" && s != "fixed" && s != "authoritycode" && s != "personaddressid";
                })
                .ToList();

            // Include allow-list filter
            HashSet<string> allowed = IncludeConfigHelper.GetEnabledIncludeKeys(_config);
            if (allowed.Count > 0)
            {
                static bool Matches(string seg, string allowedKey)
                {
                    return seg.Equals(allowedKey, StringComparison.OrdinalIgnoreCase)
                        || seg.StartsWith(allowedKey, StringComparison.OrdinalIgnoreCase)
                        || allowedKey.StartsWith(seg, StringComparison.OrdinalIgnoreCase);
                }

                filtered = filtered.Where(p =>
                {
                    if (string.IsNullOrWhiteSpace(p.Key)) return false;
                    string key = p.Key;
                    int dot = key.IndexOf('.');
                    string seg = dot >= 0 ? key[..dot] : key;
                    int bpos = seg.IndexOf('[');
                    if (bpos >= 0) seg = seg[..bpos];
                    foreach (string a in allowed)
                    {
                        if (Matches(seg, a)) return true;
                    }
                    return false;
                }).ToList();
            }

            string? first = filtered.FirstOrDefault(p => p.Key.EndsWith(".FirstName", StringComparison.OrdinalIgnoreCase)).Value;
            string? last = filtered.FirstOrDefault(p => p.Key.EndsWith(".LastName", StringComparison.OrdinalIgnoreCase)).Value;
            string selectedNameSuffix = (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
                ? loc["SelectedNameSuffixFormat", string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)))]
                : string.Empty;

            return (filtered, _parser.PrettyFormatXml(xml), selectedNameSuffix);
        }
    }
}
