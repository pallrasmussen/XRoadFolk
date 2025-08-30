using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace XRoadFolkRaw.Lib.Options
{
    public static class IncludeConfigHelper
    {
        // Returns the enabled include keys based on configuration:
        // - Operations:GetPerson:Request:Include boolean switches
        // - Operations:GetPerson:Request Include flags (GetPersonRequestOptions.Include)
        public static HashSet<string> GetEnabledIncludeKeys(IConfiguration config, bool includeSynonyms = true)
        {
            ArgumentNullException.ThrowIfNull(config);

            HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);

            IConfigurationSection incSec = config.GetSection("Operations:GetPerson:Request:Include");
            foreach (IConfigurationSection c in incSec.GetChildren())
            {
                if (bool.TryParse(c.Value, out bool on) && on)
                {
                    set.Add(c.Key);
                }
            }

            GetPersonRequestOptions? req = config.GetSection("Operations:GetPerson:Request").Get<GetPersonRequestOptions>();
            if (req is not null && req.Include != GetPersonInclude.None)
            {
                foreach (GetPersonInclude flag in Enum.GetValues<GetPersonInclude>())
                {
                    if (flag == GetPersonInclude.None) continue;
                    if ((req.Include & flag) == flag)
                    {
                        string? name = Enum.GetName(flag);
                        if (!string.IsNullOrEmpty(name)) set.Add(name);
                    }
                }
            }

            if (includeSynonyms)
            {
                // Common synonyms
                if (set.Contains("Ssn")) set.Add("SSN");
            }

            return set;
        }
    }
}
