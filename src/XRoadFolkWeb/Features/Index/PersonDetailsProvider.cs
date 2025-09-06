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

        /// <summary>
        /// Cache of allowed include keys for this scoped instance (request)
        /// </summary>
        private IReadOnlyList<string>? _allowedIncludeKeysCache;
        private IReadOnlyList<string> GetAllowedIncludeKeysFromConfig()
        {
            if (_allowedIncludeKeysCache is { Count: > 0 })
            {
                return _allowedIncludeKeysCache;
            }

            // Base keys (config-backed cache) + ensure core sections
            var baseKeys = IncludeConfigHelper.GetEnabledIncludeKeys(_config);
            HashSet<string> tmp = new(baseKeys, StringComparer.OrdinalIgnoreCase);
            _ = tmp.Add("Person");
            _ = tmp.Add("Names");
            _allowedIncludeKeysCache = [.. tmp];
            return _allowedIncludeKeysCache;
        }

        public async Task<(IReadOnlyList<(string Key, string Value)> Details, string Pretty, string SelectedNameSuffix)> GetAsync(
            string publicId,
            Microsoft.Extensions.Localization.IStringLocalizer loc,
            IReadOnlyCollection<string>? allowedIncludeKeys = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(loc);

            string xml = await _service.GetPersonAsync(publicId, ct).ConfigureAwait(false);
            IReadOnlyList<(string Key, string Value)> pairs = _parser.FlattenResponse(xml);

            List<(string Key, string Value)> filtered = [.. ApplyPrimaryFilter(pairs)];

            // Include allow-list filter (use provided keys if present, else from cached configuration)
            IReadOnlyCollection<string> allowedBase = (allowedIncludeKeys?.Count > 0)
                ? allowedIncludeKeys
                : GetAllowedIncludeKeysFromConfig();

            filtered = [.. ApplyAllowedFilter(filtered, allowedBase)];

            string selectedNameSuffix = ComputeSelectedNameSuffix(filtered, loc);

            return (filtered.AsReadOnly(), _parser.PrettyFormatXml(xml), selectedNameSuffix);
        }

        private static IEnumerable<(string Key, string Value)> ApplyPrimaryFilter(IEnumerable<(string Key, string Value)> pairs)
        {
            foreach (var p in pairs)
            {
                if (string.IsNullOrEmpty(p.Key))
                {
                    yield return p;
                    continue;
                }

                string k = p.Key;
                bool isHeaderOrBody = k.StartsWith("requestheader", StringComparison.OrdinalIgnoreCase)
                    || k.StartsWith("requestbody", StringComparison.OrdinalIgnoreCase)
                    || k.Contains(".requestheader", StringComparison.OrdinalIgnoreCase)
                    || k.Contains(".requestbody", StringComparison.OrdinalIgnoreCase);

                if (isHeaderOrBody)
                {
                    continue;
                }

                string key = p.Key;
                int lastDot = key.LastIndexOf('.');
                string sub = lastDot >= 0 ? key[(lastDot + 1)..] : key;
                int bpos = sub.IndexOf('[', StringComparison.Ordinal);
                if (bpos >= 0)
                {
                    sub = sub[..bpos];
                }

                // Case-insensitive checks without allocating lowercase copies
                if (sub.Equals("id", StringComparison.OrdinalIgnoreCase)
                    || sub.Equals("fixed", StringComparison.OrdinalIgnoreCase)
                    || sub.Equals("authoritycode", StringComparison.OrdinalIgnoreCase)
                    || sub.Equals("personaddressid", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return p;
            }
        }

        private static IEnumerable<(string Key, string Value)> ApplyAllowedFilter(IEnumerable<(string Key, string Value)> items, IReadOnlyCollection<string> allowedBase)
        {
            // Enforce core allow-list on caller-provided keys as well
            HashSet<string> allowed = new(allowedBase, StringComparer.OrdinalIgnoreCase)
            {
                "Person",
                "Names",
            };

            static bool Matches(string seg, string allowedKey)
            {
                return seg.Equals(allowedKey, StringComparison.OrdinalIgnoreCase)
                    || seg.StartsWith(allowedKey, StringComparison.OrdinalIgnoreCase)
                    || allowedKey.StartsWith(seg, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var p in items)
            {
                if (string.IsNullOrWhiteSpace(p.Key))
                {
                    continue;
                }

                foreach (string seg in Segments(p.Key))
                {
                    foreach (string a in allowed)
                    {
                        if (Matches(seg, a))
                        {
                            yield return p;
                            goto NextItem;
                        }
                    }
                }

            NextItem:;
            }
        }

        private static string ComputeSelectedNameSuffix(IReadOnlyList<(string Key, string Value)> filtered, Microsoft.Extensions.Localization.IStringLocalizer loc)
        {
            // Extract first and last names in a single pass to avoid duplicate scans
            string? first = null;
            string? last = null;
            foreach (var p in filtered)
            {
                if (first is null && p.Key.EndsWith(".FirstName", StringComparison.OrdinalIgnoreCase))
                {
                    first = p.Value;
                    if (last is not null)
                    {
                        break;
                    }
                }
                else if (last is null && p.Key.EndsWith(".LastName", StringComparison.OrdinalIgnoreCase))
                {
                    last = p.Value;
                    if (first is not null)
                    {
                        break;
                    }
                }
            }

            return (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
                ? loc["SelectedNameSuffixFormat", string.Join(' ', new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)))]
                : string.Empty;
        }

        // Existing span-free iterator version (safe for yield)
        private static IEnumerable<string> Segments(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                yield break;
            }

            int start = 0;
            int n = key.Length;
            while (start < n)
            {
                int dot = key.IndexOf('.', start);
                int endExclusive = dot >= 0 ? dot : n;

                // Trim whitespace on both sides of the segment
                int i = start;
                int j = endExclusive - 1;
                while (i <= j && char.IsWhiteSpace(key[i]))
                {
                    i++;
                }
                while (j >= i && char.IsWhiteSpace(key[j]))
                {
                    j--;
                }

                if (i <= j)
                {
                    int bracket = key.IndexOf('[', i, j - i + 1);
                    int segEnd = (bracket >= 0 && bracket <= j) ? bracket : (j + 1);
                    if (segEnd > i)
                    {
                        yield return key.Substring(i, segEnd - i);
                    }
                }

                if (dot < 0)
                {
                    break;
                }
                start = dot + 1;
            }
        }
    }
}
