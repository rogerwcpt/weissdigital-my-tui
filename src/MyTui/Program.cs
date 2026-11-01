using MyTui.UI;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

Application.SetDefaultKeyBinding(Command.Quit, Bind.All(Key.Q.WithCtrl));

IApplication app = Application.Create();
SessionScreen? screen = null;
try
{
    app.Init();
    HostTheme.Apply();
    var window = new Window();
    screen = new SessionScreen(app);
    screen.AttachTitle(window);
    window.Add(screen.Sessions, screen.Content);
    window.Initialized += (_, _) => screen.Start();
    app.Run(window);
}
finally
{
    screen?.Dispose();
    app.Dispose();
}
