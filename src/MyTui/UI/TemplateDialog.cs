using System.Collections.ObjectModel;
using MyTui.Persistence;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

internal enum TemplateAction
{
    Cancel,
    Apply,
    Save,
    Remove,
}

internal static class TemplateDialog
{
    internal static TemplateAction Ask(IApplication app, IReadOnlyList<SessionTemplate> templates, out int index)
    {
        index = -1;
        var dialog = new Dialog
        {
            Title = "Templates",
            Width = 68,
            Height = 16,
        };
        var hint = new Label
        {
            Text = templates.Count == 0
                ? "No templates. Save stores the focused session."
                : "Apply fills a new session. It does not start one.",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = false,
        };
        var names = new ObservableCollection<string>(templates.Select(template => template.Name));
        var list = new TemplateList
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };
        list.SetSource(names);
        dialog.Add(hint, list);
        CloseDialog.CancelOnEsc(dialog, app);
        dialog.AddButton(new Button { Text = "Remove" });
        dialog.AddButton(new Button { Text = "Save" });
        dialog.AddButton(new Button { Text = "Cancel" });
        dialog.AddButton(new Button { Text = "Apply" });
        dialog.Accepting += (_, args) =>
        {
            if (templates.Count == 0)
            {
                hint.Text = "Save the focused session first.";
                args.Handled = true;
            }
        };
        list.Chosen += (_, _) =>
        {
            if (templates.Count == 0)
            {
                hint.Text = "Save the focused session first.";
                return;
            }

            dialog.Result = 3;
            app.RequestStop(dialog);
        };
        dialog.Initialized += (_, _) =>
        {
            if (templates.Count > 0)
            {
                list.SelectedItem = 0;
                list.SetFocus();
            }
        };

        try
        {
            app.Run(dialog);
            int? result = dialog.Result;
            int selected = list.SelectedItem ?? -1;
            if (selected < 0 && templates.Count > 0)
            {
                selected = 0;
            }

            index = selected;
            return result switch
            {
                0 => TemplateAction.Remove,
                1 => TemplateAction.Save,
                3 => TemplateAction.Apply,
                _ => TemplateAction.Cancel,
            };
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private sealed class TemplateList : ListView
    {
        internal event EventHandler? Chosen;

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.Enter)
            {
                Chosen?.Invoke(this, EventArgs.Empty);
                return true;
            }

            return base.OnKeyDown(key);
        }
    }
}
