using System.Collections.Concurrent;
using System.Reflection;
using Terminal.Gui.App;

namespace MyTui.UI;

/// <summary>
/// Whether console input is already queued. The queue is filled on the input thread and drained
/// after the iteration event, and it is not on <see cref="IApplication"/>. Seeing it here lets the
/// fast cap apply to the sleep that follows the key, which is the sleep the echo would otherwise wait on.
/// </summary>
internal static class PendingInput
{
    private static PropertyInfo? _coordinator;
    private static FieldInfo? _queue;
    private static PropertyInfo? _empty;
    private static bool _unavailable;

    internal static bool CanSeeQueue
    {
        get
        {
            Type? coordinator = typeof(IApplication).Assembly.GetType("Terminal.Gui.App.MainLoopCoordinator`1");
            FieldInfo? queueField = coordinator?.GetField("_inputQueue", BindingFlags.Instance | BindingFlags.NonPublic);
            if (queueField?.FieldType.IsGenericType != true
                || queueField.FieldType.GetGenericTypeDefinition() != typeof(ConcurrentQueue<>))
            {
                return false;
            }

            Type? impl = typeof(IApplication).Assembly.GetType("Terminal.Gui.App.ApplicationImpl");
            PropertyInfo? property = impl?.GetProperty(
                "Coordinator",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetMethod is not null;
        }
    }

    internal static bool Any(IApplication app)
    {
        if (_unavailable)
        {
            return false;
        }

        try
        {
            if (!Bind(app))
            {
                return false;
            }

            object? coordinator = _coordinator!.GetValue(app);
            if (coordinator is null || _queue is null || _empty is null)
            {
                return false;
            }

            object? queue = _queue.GetValue(coordinator);
            return queue is not null && _empty.GetValue(queue) is false;
        }
        catch (Exception ex) when (ex is TargetException or TargetInvocationException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static bool Bind(IApplication app)
    {
        if (_queue is not null)
        {
            return true;
        }

        _coordinator ??= app.GetType().GetProperty(
            "Coordinator",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (_coordinator is null)
        {
            _unavailable = true;
            return false;
        }

        object? coordinator = _coordinator.GetValue(app);
        if (coordinator is null)
        {
            return false;
        }

        FieldInfo? field = coordinator.GetType().GetField("_inputQueue", BindingFlags.Instance | BindingFlags.NonPublic);
        PropertyInfo? empty = field?.FieldType.GetProperty("IsEmpty");
        if (field is null || empty is null || field.FieldType.ContainsGenericParameters)
        {
            _unavailable = true;
            return false;
        }

        _queue = field;
        _empty = empty;
        return true;
    }
}
