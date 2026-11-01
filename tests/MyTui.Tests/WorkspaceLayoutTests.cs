using System.Text;
using MyTui.UI;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace MyTui.Tests;

public class WorkspaceLayoutTests
{
    [Fact]
    public void NormalLayoutShowsSessionsAndTheTabs()
    {
        WorkspaceLayout layout = WorkspaceLayout.Compute(false, false, false, MaximizedPanel.None);

        Assert.True(layout.SessionsVisible);
        Assert.False(layout.SessionsFill);
        Assert.True(layout.ContentVisible);
        Assert.True(layout.TabsVisible);
        Assert.False(layout.PreviewVisible);
    }

    [Fact]
    public void HidingSessionsGivesTheContentTheFullWidth()
    {
        WorkspaceLayout layout = WorkspaceLayout.Compute(true, false, true, MaximizedPanel.None);

        Assert.False(layout.SessionsVisible);
        Assert.True(layout.ContentVisible);
        Assert.True(layout.PreviewVisible);
        Assert.False(layout.PreviewFill);
    }

    [Fact]
    public void MaximizedContentHidesSessionsAndThePreview()
    {
        WorkspaceLayout layout = WorkspaceLayout.Compute(false, false, true, MaximizedPanel.Content);

        Assert.False(layout.SessionsVisible);
        Assert.True(layout.ContentVisible);
        Assert.True(layout.TabsVisible);
        Assert.False(layout.PreviewVisible);
    }

    [Fact]
    public void MaximizedPreviewFillsTheContentAndHidesTheTabs()
    {
        WorkspaceLayout layout = WorkspaceLayout.Compute(false, false, true, MaximizedPanel.Preview);

        Assert.False(layout.SessionsVisible);
        Assert.True(layout.ContentVisible);
        Assert.False(layout.TabsVisible);
        Assert.True(layout.PreviewVisible);
        Assert.True(layout.PreviewFill);
    }

    [Fact]
    public void MaximizedSessionsFillsAndHidesTheContent()
    {
        WorkspaceLayout layout = WorkspaceLayout.Compute(false, false, true, MaximizedPanel.Sessions);

        Assert.True(layout.SessionsVisible);
        Assert.True(layout.SessionsFill);
        Assert.False(layout.ContentVisible);
        Assert.False(layout.PreviewVisible);
    }

    [Fact]
    public void HidingTheContentLeavesSessionsFullWidth()
    {
        WorkspaceLayout layout = WorkspaceLayout.Compute(false, true, true, MaximizedPanel.None);

        Assert.True(layout.SessionsVisible);
        Assert.True(layout.SessionsFill);
        Assert.False(layout.ContentVisible);
    }
}

public class PanelKeyTests
{
    [Fact]
    public void CtrlTabCyclesForwardAndBack()
    {
        Assert.True(SessionScreen.IsPanelCycle(Key.Tab.WithCtrl));
        Assert.False(Key.Tab.WithCtrl.IsShift);
        Assert.True(SessionScreen.IsPanelCycle(Key.Tab.WithCtrl.WithShift));
        Assert.True(Key.Tab.WithCtrl.WithShift.IsShift);
        Assert.False(SessionScreen.IsPanelCycle(Key.Tab));
        Assert.False(SessionScreen.IsPanelCycle(Key.Tab.WithShift));
        Assert.False(SessionScreen.IsPanelCycle(Key.Tab.WithCtrl.WithAlt));
    }

    [Fact]
    public void F4HidesSessionsAndF11Maximizes()
    {
        Assert.True(SessionScreen.IsHideSessions(Key.F4));
        Assert.False(SessionScreen.IsHideSessions(Key.F4.WithCtrl));
        Assert.False(SessionScreen.IsHideSessions(Key.F4.WithShift));
        Assert.True(SessionScreen.IsMaximizePanel(Key.F11));
        Assert.False(SessionScreen.IsMaximizePanel(Key.F11.WithAlt));
        Assert.False(SessionScreen.IsMaximizePanel(Key.F6));
    }
}

public class ActiveRowTests
{
    [Fact]
    public void CloseMarkSitsInTheLastColumn()
    {
        string row = SessionScreen.ActiveRow("shell · stopped", 26);

        Assert.Equal(26, row.EnumerateRunes().Count());
        Assert.EndsWith("\u00d7", row);
        Assert.StartsWith("shell · stopped", row);
    }

    [Fact]
    public void LongNameKeepsTheCloseMark()
    {
        string row = SessionScreen.ActiveRow(new string('n', 40), 10);

        Assert.Equal(10, row.EnumerateRunes().Count());
        Assert.EndsWith("\u00d7", row);
    }
}

public class PanelCaptionTests
{
    [Fact]
    public void IconsCallMaximizeAndHide()
    {
        var frame = new FrameView { Title = "Sessions", Width = 28, Height = 8 };
        int maximized = 0;
        int hidden = 0;
        PanelCaption caption = PanelCaption.Attach(frame, () => maximized++, () => hidden++);

        caption.Activate(0);
        caption.Activate(1);
        caption.Activate(3);

        Assert.Equal(1, maximized);
        Assert.Equal(1, hidden);
        Assert.Equal(PanelCaption.MaximizeGlyph, caption.Glyph);
        caption.Maximized = true;
        Assert.Equal(PanelCaption.RestoreGlyph, caption.Glyph);
        Assert.True(frame.Layout(new System.Drawing.Size(28, 8)));
        Assert.Equal(1, caption.Frame.X);
        Assert.Equal(0, caption.Frame.Y);
        Assert.True(caption.Frame.Width >= 2);
    }
}
