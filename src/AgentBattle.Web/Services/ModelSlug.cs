using System.Text;

namespace AgentBattle.Web.Services;

public static class ModelSlug
{
    public static string For(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        bool lastDash = true;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        if (sb.Length > 0 && sb[^1] == '-') sb.Length--;
        return sb.ToString();
    }
}
