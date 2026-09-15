using System.Text;

namespace SmartLayoutSwitcher.Native;

/// <summary>
/// Converts text by preserving the physical key and modifier that produced each
/// character in the source layout, then asking Windows for that key in the target
/// layout. Unmappable characters are intentionally left unchanged.
/// </summary>
public sealed class KeyboardLayoutTranslator
{
    public string Convert(string text, IntPtr sourceHkl, IntPtr targetHkl)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || sourceHkl == IntPtr.Zero || targetHkl == IntPtr.Zero)
            return text;

        var result = new StringBuilder(text.Length);
        foreach (var character in text)
            result.Append(ConvertCharacter(character, sourceHkl, targetHkl));
        return result.ToString();
    }

    private static string ConvertCharacter(char character, IntPtr sourceHkl, IntPtr targetHkl)
    {
        var mapping = NativeMethods.VkKeyScanExW(character, sourceHkl);
        if (mapping == -1)
            return character.ToString();

        var virtualKey = (byte)(mapping & 0xFF);
        var modifiers = (byte)((mapping >> 8) & 0xFF);
        var source = TranslateKey(virtualKey, modifiers, sourceHkl);
        if (source != character.ToString())
            return character.ToString();

        var target = TranslateKey(virtualKey, modifiers, targetHkl);
        return string.IsNullOrEmpty(target) ? character.ToString() : target;
    }

    private static string TranslateKey(byte virtualKey, byte modifiers, IntPtr hkl)
    {
        var keyState = new byte[256];
        if ((modifiers & 1) != 0)
            keyState[NativeMethods.VK_SHIFT] = 0x80;
        if ((modifiers & 2) != 0)
            keyState[NativeMethods.VK_CONTROL] = 0x80;
        if ((modifiers & 4) != 0)
            keyState[NativeMethods.VK_MENU] = 0x80;

        var buffer = new StringBuilder(8);
        var scanCode = NativeMethods.MapVirtualKeyExW(virtualKey, 0, hkl);
        var count = NativeMethods.ToUnicodeEx(virtualKey, scanCode, keyState, buffer, buffer.Capacity, 0, hkl);
        if (count < 0)
        {
            // A dead key changes ToUnicodeEx state; clear it before handling the
            // next character and keep this character unchanged.
            NativeMethods.ToUnicodeEx(NativeMethods.VK_SPACE, 0, new byte[256], buffer, buffer.Capacity, 0, hkl);
            return string.Empty;
        }

        return count > 0 ? buffer.ToString(0, count) : string.Empty;
    }
}
