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
    var window = new Window { Title = "my-tui  —  Ctrl+N new   Ctrl+O focus   Ctrl+T tab   Ctrl+E edit   Del close   Ctrl+Q quit" };
    screen = new SessionScreen(app);
    window.Add(screen.Sessions, screen.Content);
    window.Initialized += (_, _) => screen.Start();
    app.Run(window);
}
finally
{
    screen?.Dispose();
    app.Dispose();
}
