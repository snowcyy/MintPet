using System.Text.RegularExpressions;

namespace MintPet
{
    /// <summary>
    /// Scrubs anything that looks like a credential out of text that is about to
    /// be shown in a bubble or written to the crash log.
    /// </summary>
    internal static class Redact
    {
        public static string Text(string text, string secret)
        {
            if (string.IsNullOrEmpty(text)) return "";

            if (!string.IsNullOrEmpty(secret) && secret.Length >= 6)
                text = text.Replace(secret, "******");

            text = Regex.Replace(text, @"(?i)bearer\s+[A-Za-z0-9._\-]+", "Bearer ******");
            text = Regex.Replace(text, @"sk-[A-Za-z0-9_\-]{8,}", "sk-******");

            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length > 180) text = text.Substring(0, 180) + "…";
            return text;
        }
    }
}
