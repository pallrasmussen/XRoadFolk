using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System.Runtime.CompilerServices;

namespace XRoadFolkRaw.Lib.Options
{
    public static class IncludeConfigHelper
    {
        private sealed class CacheState(HashSet<string> baseKeys, IDisposable? registration)
        {
            public HashSet<string> BaseKeys { get; } = baseKeys;
            public IDisposable? Registration { get; } = registration; // kept to keep callback alive
        }

        private static readonly ConditionalWeakTable<IConfiguration, CacheState> Cache = [];

        // Returns the enabled include keys based on configuration:
        // - Operations:GetPerson:Request:Include boolean switches
        // - Operations:GetPerson:Request Include flags (GetPersonRequestOptions.Include)
        public static HashSet<string> GetEnabledIncludeKeys(IConfiguration config, bool includeSynonyms = true)
        {
            ArgumentNullException.ThrowIfNull(config);

            CacheState state = Cache.GetValue(config, BuildState);

            // Return a copy so callers cannot mutate cached set
            HashSet<string> result = new(state.BaseKeys, StringComparer.OrdinalIgnoreCase);
            if (includeSynonyms)
            {
                ApplySynonyms(result);
            }
            return result;
        }

        private static CacheState BuildState(IConfiguration config)
        {
            HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);

            IConfigurationSection incSec = config.GetSection("Operations:GetPerson:Request:Include");
            foreach (IConfigurationSection c in incSec.GetChildren())
            {
                if (bool.TryParse(c.Value, out bool on) && on)
                {
                    _ = set.Add(c.Key);
                }
            }

            GetPersonRequestOptions? req = config.GetSection("Operations:GetPerson:Request").Get<GetPersonRequestOptions>();
            if (req is not null && req.Include != GetPersonInclude.None)
            {
                foreach (GetPersonInclude flag in Enum.GetValues<GetPersonInclude>())
                {
                    if (flag == GetPersonInclude.None)
                    {
                        continue;
                    }

                    if ((req.Include & flag) == flag)
                    {
                        string? name = Enum.GetName(flag);
                        if (!string.IsNullOrEmpty(name))
                        {
                            _ = set.Add(name);
                        }
                    }
                }
            }

            // Invalidate cache when configuration reloads
            IChangeToken token = config.GetReloadToken();
            IDisposable? registration = token.RegisterChangeCallback(static state =>
            {
                try
                {
                    IConfiguration cfg = (IConfiguration)state!;
                    _ = Cache.Remove(cfg);
                }
                catch { }
            }, config);

            return new CacheState(set, registration);
        }

        private static void ApplySynonyms(HashSet<string> set)
        {
            if (set.Contains("Ssn"))
            {
                _ = set.Add("SSN");
            }
        }
    }
}
