using MyTui.UI;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

Application.SetDefaultKeyBinding(Command.Quit, Bind.All(Key.Q.WithCtrl));
// The default is 25. A key waits one iteration to reach the PTY and the echo waits for the
// next, so 40 ms iterations read as typing lag. FramePacer keeps steady output near 60 paints.
Application.MaximumIterationsPerSecond = 120;

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
