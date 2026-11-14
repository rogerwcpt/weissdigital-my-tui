using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace MyTui.UI;

/// <summary>
/// Terminal.Gui's kitty parser keeps Shift, Ctrl, and Alt, and drops Super. On macOS that is
/// the Command key, so Cmd+K would otherwise arrive as the letter k. This puts Super back on
/// that one chord before the key is delivered.
/// </summary>
internal static class CommandChord
{
    internal static bool Install(IApplication app)
    {
        IInputProcessor? processor = app.Driver?.GetInputProcessor();
        if (processor is null)
        {
            return false;
        }

        object parser = processor.GetParser();
        FieldInfo? keyboardField = parser.GetType().GetField(
            "_keyboardParser",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (keyboardField?.GetValue(parser) is not AnsiKeyboardParser keyboard)
        {
            return false;
        }

        FieldInfo? patternsField = typeof(AnsiKeyboardParser).GetField(
            "_patterns",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (patternsField?.GetValue(keyboard) is not List<AnsiKeyboardParserPattern> patterns)
        {
            return false;
        }

        bool replaced = false;
        for (int i = 0; i < patterns.Count; i++)
        {
            if (patterns[i] is KittyKeyboardPattern and not SuperKeyPattern)
            {
                patterns[i] = new SuperKeyPattern();
                replaced = true;
            }
        }

        return replaced || patterns.OfType<SuperKeyPattern>().Any();
    }

    internal static Key? Parse(string sequence) => new SuperKeyPattern().GetKey(sequence);

    /// <summary>Kitty CSI-u for Cmd+K, with or without the alternate-key and event-type fields.</summary>
    private sealed class SuperKeyPattern : KittyKeyboardPattern
    {
        private static readonly Regex Sequence = new(
            @"^\u001b\[(\d+)(?::(\d*))?(?::(\d*))?(?:;([^;u]*))?(?:;([^u]+))?u$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        protected override Key? GetKeyImpl(string? input)
        {
            Key? key = base.GetKeyImpl(input);
            if (key is null || key.IsAlt || key.IsCtrl || key.IsShift || key != Key.K || !HasSuperOnly(input))
            {
                return key;
            }

            return new Key(key) { ModifierKey = ModifierKey.Super };
        }

        private static bool HasSuperOnly(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            Match match = Sequence.Match(input);
            if (!match.Success)
            {
                return false;
            }

            string modifierField = match.Groups[4].Value;
            if (modifierField.Length == 0)
            {
                return false;
            }

            string token = modifierField.Split(':')[0];
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int encoded) || encoded < 1)
            {
                return false;
            }

            // Kitty: shift 1, alt 2, ctrl 4, super 8, then hyper, meta, caps lock, num lock.
            int modifiers = (encoded - 1) & ~(64 | 128);
            return modifiers == 8;
        }
    }
}
