using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System.Runtime.CompilerServices;
using System.Diagnostics;

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

        /// <summary>
        /// Returns the enabled include keys based on configuration:
        /// - Operations:GetPerson:Request:Include boolean switches
        /// - Operations:GetPerson:Request Include flags (GetPersonRequestOptions.Include)
        /// Returns a read-only view to avoid accidental mutation by callers.
        /// </summary>
        public static IReadOnlySet<string> GetEnabledIncludeKeys(IConfiguration config, bool includeSynonyms = true)
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

            // Invalidate cache when configuration reloads and dispose registration
            IChangeToken token = config.GetReloadToken();
            IDisposable? registration = token.RegisterChangeCallback(static state =>
            {
                try
                {
                    IConfiguration cfg = (IConfiguration)state!;
                    if (Cache.TryGetValue(cfg, out CacheState? cs))
                    {
                        cs.Registration?.Dispose();
                    }
                    _ = Cache.Remove(cfg);
                }
                catch (Exception ex)
                {
                    Trace.TraceError("IncludeConfigHelper: Failed to invalidate cache on config reload: {0}", ex);
                }
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
