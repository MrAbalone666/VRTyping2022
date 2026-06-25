using System;

namespace VRTyping.Keyboard
{
    public static class VRKeyboardKeyUtility
    {
        public const string RootPrefix = "Root_";

        public static string GetKeyId(VRKeyboardKey key)
        {
            return key == null ? string.Empty : StripRootPrefix(key.name);
        }

        public static string StripRootPrefix(string keyName)
        {
            if (string.IsNullOrEmpty(keyName))
                return string.Empty;

            return keyName.StartsWith(RootPrefix, StringComparison.Ordinal)
                ? keyName.Substring(RootPrefix.Length)
                : keyName;
        }

        public static bool TryGetLetterKey(VRKeyboardKey key, out char letter)
        {
            letter = default;
            var keyId = GetKeyId(key);
            if (keyId.Length != 1 || !char.IsLetter(keyId[0]))
                return false;

            letter = char.ToLowerInvariant(keyId[0]);
            return true;
        }
    }
}
