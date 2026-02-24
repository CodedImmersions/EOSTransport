using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Epic.OnlineServices;

using Epic.OnlineServices.Lobby;

namespace EpicTransport
{
    public static class Helper
    {
        private static readonly Regex puidR = new Regex(@"^[0-9a-fA-F]{32}$", RegexOptions.Compiled);
        private static readonly Regex urlSafeR = new Regex(@"^[A-Za-z0-9\-\._~]*$", RegexOptions.Compiled);

        public const char PaddingCharacter = '\u200B';


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

        /// <summary>
        /// Adds padding characters to equal <see cref="LobbyInterface.MIN_LOBBYIDOVERRIDE_LENGTH"/>, that way you can create lobbies with 1-3 characters.
        /// </summary>
        public static string ToEOSString(string input)
        {
            if (input.Length >= LobbyInterface.MIN_LOBBYIDOVERRIDE_LENGTH) return input;
            else return input + new string(PaddingCharacter, LobbyInterface.MIN_LOBBYIDOVERRIDE_LENGTH - input.Length);
        }

        /// <summary>
        /// Removes the padding characters to turn it back into the inputted string.
        /// </summary>
        public static string FromEOSString(string input) => input.TrimEnd(PaddingCharacter);
    }
}
