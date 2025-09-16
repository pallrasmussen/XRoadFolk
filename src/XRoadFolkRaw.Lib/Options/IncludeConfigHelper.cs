using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace XRoadFolkRaw.Lib.Options
{
    /// <summary>
    /// Utility to read enabled include keys for GetPerson from configuration.
    /// Caches results per <see cref="IConfiguration"/> instance and invalidates on reload.
    /// </summary>
    public static class IncludeConfigHelper
    {
        private sealed class CacheState(HashSet<string> baseKeys, IDisposable? registration)
        {
            public HashSet<string> BaseKeys { get; } = baseKeys;
            public IDisposable? Registration { get; } = registration; // kept to keep callback alive
        }

        private sealed class CallbackState(IConfiguration config, ILogger? logger)
        {
            public IConfiguration Config { get; } = config;
            public ILogger? Logger { get; } = logger;
        }

        private static readonly ConditionalWeakTable<IConfiguration, CacheState> _cache = [];

        /// <summary>
        /// Returns the enabled include keys based on configuration:
        /// - Operations:GetPerson:Request:Include boolean switches
        /// - Operations:GetPerson:Request Include flags (GetPersonRequestOptions.Include)
        /// Returns a read-only view to avoid accidental mutation by callers.
        /// </summary>
        public static IReadOnlySet<string> GetEnabledIncludeKeys(IConfiguration config, bool includeSynonyms = true, ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(config);

            CacheState state;
            if (!_cache.TryGetValue(config, out state!))
            {
                state = BuildState(config, logger);
                _cache.Add(config, state);
            }

            // Return a copy so callers cannot mutate cached set
            HashSet<string> result = new(state.BaseKeys, StringComparer.OrdinalIgnoreCase);
            if (includeSynonyms)
            {
                ApplySynonyms(result);
            }
            return result;
        }

        private static CacheState BuildState(IConfiguration config, ILogger? logger)
        {
            HashSet<string> set = CollectIncludeSwitches(config);
            ExpandFlags(config, set);

            // Invalidate cache when configuration reloads and dispose registration
            IChangeToken token = config.GetReloadToken();
            IDisposable? registration = token.RegisterChangeCallback(static stateObj =>
            {
                try
                {
                    CallbackState st = (CallbackState)stateObj!;
                    if (_cache.TryGetValue(st.Config, out CacheState? cs))
                    {
                        cs.Registration?.Dispose();
                    }
                    _ = _cache.Remove(st.Config);
                }
                catch (Exception ex)
                {
                    try
                    {
                        CallbackState st = (CallbackState)stateObj!;
                        st.Logger?.LogError(ex, "IncludeConfigHelper: Failed to invalidate cache on config reload");
                    }
                    catch
                    {
                        // Swallow secondary failures
                    }
                }
            }, new CallbackState(config, logger));

            return new CacheState(set, registration);
        }

        private static HashSet<string> CollectIncludeSwitches(IConfiguration config)
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
            return set;
        }

        private static void ExpandFlags(IConfiguration config, HashSet<string> set)
        {
            GetPersonRequestOptions? req = config.GetSection("Operations:GetPerson:Request").Get<GetPersonRequestOptions>();
            if (req is null || req.Include == GetPersonInclude.None)
            {
                return;
            }

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

        private static void ApplySynonyms(HashSet<string> set)
        {
            if (set.Contains("Ssn"))
            {
                _ = set.Add("SSN");
            }
        }
    }
}
