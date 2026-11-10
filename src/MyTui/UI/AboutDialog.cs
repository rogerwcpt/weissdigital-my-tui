using System.Reflection;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiAttribute = global::Terminal.Gui.Drawing.Attribute;

namespace MyTui.UI;

internal static class AboutDialog
{
    private const int ContentWidth = 48;

    internal static void Show(IApplication app)
    {
        var dialog = new Dialog
        {
            Title = "About",
            Width = ContentWidth + 8,
            Height = AboutArt.Lines.Count + 7,
        };
        dialog.Add(new AboutArt
        {
            X = 2,
            Y = 1,
            Width = Dim.Fill(2),
            Height = AboutArt.Lines.Count,
        });
        dialog.AddButton(new Button { Text = "Close" });
        CloseDialog.CancelOnEsc(dialog, app);
        try
        {
            app.Run(dialog);
        }
        finally
        {
            dialog.Dispose();
        }
    }

    internal static string Version()
    {
        Assembly assembly = typeof(AboutDialog).Assembly;
        string? info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        return assembly.GetName().Version?.ToString(3) ?? "dev";
    }

    /// <summary>Draws the about text, every line centered, with the theme's accent and muted roles.</summary>
    private sealed class AboutArt : View
    {
        internal enum Tone
        {
            Accent,
            Title,
            Body,
            Muted,
        }

        internal static readonly IReadOnlyList<(string Text, Tone Tone)> Lines =
        [
            ("╭──┬──────┬──╮", Tone.Accent),
            ("│  │  ▮   │  │", Tone.Accent),
            ("╰──┴──────┴──╯", Tone.Accent),
            ("", Tone.Body),
            ("m y - t u i", Tone.Title),
            ("v" + Version(), Tone.Muted),
            ("", Tone.Body),
            ("A terminal-first session orchestrator.", Tone.Body),
            ("", Tone.Body),
            ("─────────  ◆  ─────────", Tone.Muted),
            ("", Tone.Body),
            ("Weiss Digital", Tone.Accent),
        ];

        public AboutArt()
        {
            CanFocus = false;
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            int width = Viewport.Width;
            for (int row = 0; row < Lines.Count && row < Viewport.Height; row++)
            {
                (string text, Tone tone) = Lines[row];
                SetAttribute(AttributeFor(tone));
                int length = 0;
                foreach (Rune _ in text.EnumerateRunes())
                {
                    length++;
                }

                int column = Math.Max(0, (width - length) / 2);
                for (int x = 0; x < width; x++)
                {
                    AddRune(x, row, new Rune(' '));
                }

                foreach (Rune rune in text.EnumerateRunes())
                {
                    if (column >= width)
                    {
                        break;
                    }

                    AddRune(column, row, rune);
                    column++;
                }
            }

            return true;
        }

        private TuiAttribute AttributeFor(Tone tone)
        {
            TuiAttribute normal = GetAttributeForRole(VisualRole.Normal);
            return tone switch
            {
                Tone.Accent => GetAttributeForRole(VisualRole.HotNormal) with { Style = TextStyle.Bold },
                Tone.Title => normal with { Style = normal.Style | TextStyle.Bold },
                Tone.Muted => GetAttributeForRole(VisualRole.Disabled),
                _ => normal,
            };
        }
    }
}
