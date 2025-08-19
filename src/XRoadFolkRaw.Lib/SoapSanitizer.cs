using System.Text.RegularExpressions;
public static class SoapSanitizer
{
    private static readonly Regex UserRx = new("<username>(.*?)</username>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex PassRx = new("<password>(.*?)</password>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TokenRx = new("<token>(.*?)</token>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    public static string Scrub(string xml, bool maskTokens = true)
    {
        xml = UserRx.Replace(xml, m => $"<username>{Mask(m.Groups[1].Value)}</username>");
        xml = PassRx.Replace(xml, m => $"<password>{Mask(m.Groups[1].Value)}</password>");
        xml = TokenRx.Replace(xml, m => maskTokens ? $"<token>{MaskToken(m.Groups[1].Value)}</token>" : $"<token>{m.Groups[1].Value}</token>");
        return xml;
    }
    private static string Mask(string s)
    {
        return string.IsNullOrEmpty(s) ? s : new string('*', 8);
    }

    private static string MaskToken(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

        string head = s.Length <= 6 ? s : s.Substring(0, 6);
        return head + "...(masked)";
    }
}