namespace Nova.Sim
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// A stable fingerprint of a saved game state: SHA-256 of the .sstate text with the
    /// machine-specific elements (GameFolder, StatePathName, SettingsPathName) blanked, so the same game played in
    /// two different folders hashes the same.
    /// </summary>
    public static class StateHasher
    {
        private static readonly Regex PathElements = new Regex("<(GameFolder|StatePathName|SettingsPathName)>[^<]*</(GameFolder|StatePathName|SettingsPathName)>", RegexOptions.Compiled);

        public static string Normalize(string stateXml)
        {
            return PathElements.Replace(stateXml, "<$1 />");
        }

        public static string HashText(string stateXml)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Normalize(stateXml));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < 12; i++)
                {
                    builder.Append(digest[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        public static string HashFile(string path)
        {
            return HashText(File.ReadAllText(path));
        }

        /// <summary>The first line where two texts differ, for failure messages.</summary>
        public static string FirstDifference(string a, string b, int context = 160)
        {
            string[] linesA = a.Replace("\r\n", "\n").Split('\n');
            string[] linesB = b.Replace("\r\n", "\n").Split('\n');
            int count = Math.Min(linesA.Length, linesB.Length);
            for (int i = 0; i < count; i++)
            {
                if (linesA[i] != linesB[i])
                {
                    return "line " + (i + 1) + ": saved '" + Trim(linesA[i], context) + "' vs re-saved '" + Trim(linesB[i], context) + "'";
                }
            }

            if (linesA.Length != linesB.Length)
            {
                return "line count " + linesA.Length + " vs " + linesB.Length;
            }

            return null;
        }

        private static string Trim(string text, int max)
        {
            text = text.Trim();
            return text.Length > max ? text.Substring(0, max) + "..." : text;
        }
    }
}
