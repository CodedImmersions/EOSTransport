using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Epic.OnlineServices;

namespace EpicTransport
{
    public static class Helper
    {
        private static readonly Regex puidR = new Regex(@"^[0-9a-fA-F]{32}$", RegexOptions.Compiled);
        private static readonly Regex urlSafeR = new Regex(@"^[A-Za-z0-9\-\._~]*$", RegexOptions.Compiled);


        public static bool IsValidPUID(ProductUserId puid) => puidR.IsMatch(puid.ToString());
        public static bool IsValidPUID(string puid) => puidR.IsMatch(puid);

        public static string GenerateHexString(int bytecount = 16) //NOTE: 1 byte = 2 characters, so the defualt of 16 bytes would be 32 characters.
        {
            byte[] bytes = new byte[bytecount];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider()) rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        public static bool IsUrlSafe(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            return urlSafeR.IsMatch(input);
        }
    }
}
