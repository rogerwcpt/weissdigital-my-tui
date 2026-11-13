using MyTui.UI;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

// Terminal.Gui needs a quit key, and its default is Esc. SessionScreen swallows this one so
// quitting goes through F10 or the F9 palette, with confirmation for running sessions.
Application.SetDefaultKeyBinding(Command.Quit, Bind.All(Key.Q.WithCtrl));
// The library default is 25. A key waits one iteration to reach the PTY and the echo waits
// for the next, so 40 ms iterations read as typing lag. SessionScreen lowers this to
// WakePace.IdlePerSecond while nothing is queued and the visible terminal is unchanged,
// and raises it to WakePace.ActivePerSecond before the loop measures its sleep. Startup
// stays on the fast cap until that first quiet iteration. FramePacer keeps steady output
// near 60 paints.
Application.MaximumIterationsPerSecond = WakePace.ActivePerSecond;

IApplication app = Application.Create();
SessionScreen? screen = null;
try
{
    app.Init();
    HostTheme.Load();
    MyTui.Terminal.TerminalPalette.Load(HostTheme.ConfigPath());
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
