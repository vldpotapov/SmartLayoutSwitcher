using System.Windows;
using SmartLayoutSwitcher.Native;

namespace SmartLayoutSwitcher.App.Services;

public sealed record SelectedTextConversionResult(bool Succeeded, int ChangedCharacters, string Message);

/// <summary>
/// Uses the standard copy/paste path so the focused application owns the selected
/// text. The original clipboard contents are restored after the paste when they
/// have not been changed by another application in the meantime.
/// </summary>
public sealed class SelectedTextConversionService
{
    private readonly KeyboardLayoutTranslator _translator = new();

    public async Task<SelectedTextConversionResult> ConvertAsync(IntPtr sourceHkl, IntPtr targetHkl)
    {
        if (sourceHkl == IntPtr.Zero || targetHkl == IntPtr.Zero)
            return new(false, 0, "Keyboard layouts are unavailable.");

        try
        {
            var originalClipboard = CaptureClipboard();
            var beforeCopy = NativeMethods.GetClipboardSequenceNumber();

            NativeMethods.SendControlShortcut(NativeMethods.VK_C);
            var selectedText = await ReadCopiedTextAsync(beforeCopy);
            if (string.IsNullOrEmpty(selectedText))
            {
                RestoreClipboardIfUnchanged(originalClipboard, NativeMethods.GetClipboardSequenceNumber());
                return new(false, 0, "No selectable text was copied.");
            }

            var convertedText = _translator.Convert(selectedText, sourceHkl, targetHkl);
            if (convertedText == selectedText)
            {
                RestoreClipboardIfUnchanged(originalClipboard, NativeMethods.GetClipboardSequenceNumber());
                return new(false, 0, "The selected text has no mappable characters.");
            }

            Clipboard.SetText(convertedText, TextDataFormat.UnicodeText);
            var stagedSequence = NativeMethods.GetClipboardSequenceNumber();
            NativeMethods.SendControlShortcut(NativeMethods.VK_V);
            await Task.Delay(100);
            RestoreClipboardIfUnchanged(originalClipboard, stagedSequence);

            var changed = selectedText.Zip(convertedText, (left, right) => left != right).Count();
            return new(true, changed, "Selected text converted.");
        }
        catch (Exception)
        {
            return new(false, 0, "The selected text could not be converted.");
        }
    }

    private static async Task<string?> ReadCopiedTextAsync(uint beforeCopy)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await Task.Delay(35);
            if (NativeMethods.GetClipboardSequenceNumber() == beforeCopy)
                continue;

            return Clipboard.ContainsText(TextDataFormat.UnicodeText)
                ? Clipboard.GetText(TextDataFormat.UnicodeText)
                : null;
        }

        return null;
    }

    private static ClipboardSnapshot CaptureClipboard()
    {
        var source = Clipboard.GetDataObject();
        if (source is null)
            return new ClipboardSnapshot(WasEmpty: true, Data: null);

        var copy = new DataObject();
        foreach (var format in source.GetFormats(autoConvert: true))
        {
            try
            {
                var value = source.GetData(format, autoConvert: true);
                if (value is not null)
                    copy.SetData(format, value);
            }
            catch
            {
                // A format that cannot be materialised is simply omitted; text,
                // files and images are still preserved when the source supplies them.
            }
        }

        return new ClipboardSnapshot(WasEmpty: false, Data: copy);
    }

    private static void RestoreClipboardIfUnchanged(ClipboardSnapshot snapshot, uint expectedSequence)
    {
        if (NativeMethods.GetClipboardSequenceNumber() != expectedSequence)
            return;

        if (snapshot.WasEmpty)
            Clipboard.Clear();
        else if (snapshot.Data is not null)
            Clipboard.SetDataObject(snapshot.Data, copy: true);
    }

    private sealed record ClipboardSnapshot(bool WasEmpty, DataObject? Data);
}
