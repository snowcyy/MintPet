using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MintPet
{
    public sealed class PetSettings
    {
        public string GifPath = "";
        public int Scale = 0;               // 0 = auto
        public int X = int.MinValue;
        public int Y = int.MinValue;

        // DeepSeek balance widget
        public string ApiKey = "";
        public string ApiBase = DeepSeekBalance.DefaultBaseUrl;
        public bool ShowBalance = true;
        public bool BalanceSound = true;
        public bool ClickSound = true;
        public int BalanceVolume = 100;
        public int ClickVolume = 100;
        public int RefreshSeconds = 300;
        public int TickIntervalMs = 300;    // how often the balance digits jump
        public string HitSound = "Classic"; // which tick blip to play
        public string BubbleStyle = "Soft"; // bubble / chip border preset

        public static string Folder
        {
            get
            {
                // Store settings next to the exe by default. MINT_PET_HOME can still
                // override this for portable / multi-instance setups.
                string custom = Environment.GetEnvironmentVariable("MINT_PET_HOME");
                if (!string.IsNullOrEmpty(custom)) return custom;
                return AppContext.BaseDirectory;
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(Folder, "settings.ini"); }
        }

        public static PetSettings Load()
        {
            PetSettings settings = new PetSettings();
            try
            {
                if (!File.Exists(FilePath)) return settings;

                string[] rows = File.ReadAllLines(FilePath, Encoding.UTF8);
                for (int i = 0; i < rows.Length; i++)
                {
                    string line = rows[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    int equals = line.IndexOf('=');
                    if (equals <= 0) continue;

                    string key = line.Substring(0, equals).Trim().ToLowerInvariant();
                    string value = line.Substring(equals + 1).Trim();

                    if (key == "gif") settings.GifPath = value;
                    else if (key == "scale") settings.Scale = ParseInt(value, 0);
                    else if (key == "x") settings.X = ParseInt(value, int.MinValue);
                    else if (key == "y") settings.Y = ParseInt(value, int.MinValue);
                    else if (key == "apikey") settings.ApiKey = DecodeKey(value);
                    else if (key == "apibase" && value.Length > 0) settings.ApiBase = value;
                    else if (key == "showbalance") settings.ShowBalance = value != "0";
                    else if (key == "balancesound") settings.BalanceSound = value != "0";
                    else if (key == "clicksound") settings.ClickSound = value != "0";
                    else if (key == "balancevolume") settings.BalanceVolume = ParseInt(value, 100);
                    else if (key == "clickvolume") settings.ClickVolume = ParseInt(value, 100);
                    else if (key == "refresh") settings.RefreshSeconds = ParseInt(value, 300);
                    else if (key == "tick") settings.TickIntervalMs = ParseInt(value, 300);
                    else if (key == "sfx" && value.Length > 0) settings.HitSound = value;
                    else if (key == "style" && value.Length > 0) settings.BubbleStyle = value;
                }
            }
            catch
            {
                // a broken settings file must never stop the pet from starting
            }

            if (settings.RefreshSeconds < 10) settings.RefreshSeconds = 10;
            // keep old 15/60-minute files from pointing at a menu item that no longer exists
            if (settings.RefreshSeconds != 60 && settings.RefreshSeconds != 120 &&
                settings.RefreshSeconds != 300 && settings.RefreshSeconds != 600 &&
                settings.RefreshSeconds != 1800)
            {
                settings.RefreshSeconds = 300;
            }
            if (settings.TickIntervalMs < 80) settings.TickIntervalMs = 80;
            if (settings.TickIntervalMs > 2000) settings.TickIntervalMs = 2000;
            if (settings.BalanceVolume < 0) settings.BalanceVolume = 0;
            if (settings.BalanceVolume > 100) settings.BalanceVolume = 100;
            if (settings.ClickVolume < 0) settings.ClickVolume = 0;
            if (settings.ClickVolume > 100) settings.ClickVolume = 100;
            return settings;
        }

        public void Save()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("# MintPet settings");
            text.AppendLine("gif=" + GifPath);
            text.AppendLine("scale=" + Scale.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("x=" + X.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("y=" + Y.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("apikey=" + EncodeKey(ApiKey));
            text.AppendLine("apibase=" + (string.IsNullOrEmpty(ApiBase) ? DeepSeekBalance.DefaultBaseUrl : ApiBase));
            text.AppendLine("showbalance=" + (ShowBalance ? "1" : "0"));
            text.AppendLine("balancesound=" + (BalanceSound ? "1" : "0"));
            text.AppendLine("clicksound=" + (ClickSound ? "1" : "0"));
            text.AppendLine("balancevolume=" + BalanceVolume.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("clickvolume=" + ClickVolume.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("refresh=" + RefreshSeconds.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("tick=" + TickIntervalMs.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("sfx=" + HitSound);
            text.AppendLine("style=" + BubbleStyle);

            string data = text.ToString();
            UTF8Encoding utf8 = new UTF8Encoding(false);

            // Settings live next to the exe (or MINT_PET_HOME), never in AppData.
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, data, utf8);
            }
            catch
            {
            }
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return parsed;
            return fallback;
        }

        /// <summary>Keys are encrypted with Windows DPAPI (current user).</summary>
        private static string EncodeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            try
            {
                byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
                return "dpapi:" + Convert.ToBase64String(encrypted);
            }
            catch
            {
                // Fail closed: never fall back to writing the key in clear text.
                NoteKeyNotPersisted();
                return "";
            }
        }

        private static void NoteKeyNotPersisted()
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                              "  无法用 DPAPI 加密 API Key，本次未写入设置文件（仅当前会话有效）。\r\n";
                File.AppendAllText(Path.Combine(Folder, "error.log"), line, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static string DecodeKey(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";

            if (stored.StartsWith("dpapi:", StringComparison.Ordinal))
            {
                try
                {
                    byte[] encrypted = Convert.FromBase64String(stored.Substring(6));
                    byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plain);
                }
                catch
                {
                    return "";
                }
            }

            if (stored.StartsWith("plain:", StringComparison.Ordinal)) return stored.Substring(6);
            return stored;
        }
    }
}
