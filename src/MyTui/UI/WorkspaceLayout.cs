namespace MyTui.UI;

internal enum MaximizedPanel
{
    None,
    Sessions,
    Content,
    Preview,
}

/// <summary>
/// Which frames are on screen. Maximize hides the others. Hiding Sessions or the session
/// frame leaves the remaining one full width.
/// </summary>
internal readonly record struct WorkspaceLayout(
    bool SessionsVisible,
    bool SessionsFill,
    bool ContentVisible,
    bool TabsVisible,
    bool PreviewVisible,
    bool PreviewFill)
{
    internal static WorkspaceLayout Compute(
        bool sessionsHidden,
        bool contentHidden,
        bool previewOpen,
        MaximizedPanel maximized)
    {
        bool sessionsVisible = maximized switch
        {
            MaximizedPanel.Sessions => true,
            MaximizedPanel.Content or MaximizedPanel.Preview => false,
            _ => !sessionsHidden,
        };

        if (contentHidden && maximized != MaximizedPanel.Content && maximized != MaximizedPanel.Preview)
        {
            sessionsVisible = true;
        }

        bool contentVisible = maximized switch
        {
            MaximizedPanel.Sessions => false,
            MaximizedPanel.Content or MaximizedPanel.Preview => true,
            _ => !contentHidden,
        };

        if (!sessionsVisible && !contentVisible)
        {
            contentVisible = true;
        }

        bool previewFill = maximized == MaximizedPanel.Preview && contentVisible;
        bool previewVisible = contentVisible
            && (previewFill || (previewOpen && maximized != MaximizedPanel.Content));
        bool tabsVisible = contentVisible && !previewFill;
        bool sessionsFill = sessionsVisible && !contentVisible;
        return new WorkspaceLayout(
            sessionsVisible,
            sessionsFill,
            contentVisible,
            tabsVisible,
            previewVisible,
            previewFill);
    }
}
