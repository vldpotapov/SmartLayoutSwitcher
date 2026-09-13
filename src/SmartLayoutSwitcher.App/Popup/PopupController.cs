using SmartLayoutSwitcher.Core;
using SmartLayoutSwitcher.Native;
using System.Windows;
using System.Windows.Threading;

namespace SmartLayoutSwitcher.App.Popup;

/// <summary>
/// Non-focus-stealing layout picker shown on long press (spec §6, §34).
/// All window interaction happens on the UI (Dispatcher) thread.
/// </summary>
public sealed class PopupController
{
    private LayoutPopupWindow? _window;

    public bool IsOpen => _window is { IsVisible: true };

    /// <summary>Fired with the chosen layout; the service performs the actual switch.</summary>
    public event Action<LayoutId>? LayoutChosen;

    public void Show(IReadOnlyList<LayoutInfo> layouts, LayoutId current, LayoutId paired)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        if (dispatcher.CheckAccess())
            ShowCore(layouts, current, paired);
        else
            dispatcher.BeginInvoke(() => ShowCore(layouts, current, paired));
    }

    private void ShowCore(IReadOnlyList<LayoutInfo> layouts, LayoutId current, LayoutId paired)
    {
        if (layouts.Count == 0)
            return;

        HideCore();

        var orderedLayouts = layouts
            .OrderBy(layout => layout.Id == current ? 0 : layout.Id == paired ? 1 : 2)
            .ThenBy(layout => layout.SortOrder)
            .ToArray();

        var window = new LayoutPopupWindow(orderedLayouts, current);
        window.LayoutChosen += id => LayoutChosen?.Invoke(id);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
                _window = null;
        };

        _window = window;
        // Capture the desktop only after the window has its final size and
        // screen position. It stays fully transparent for that one render
        // frame, so the capture contains the application underneath, not the
        // popup itself.
        window.Opacity = 0;
        window.Show();
        window.Dispatcher.BeginInvoke(() =>
        {
            if (!window.IsVisible)
                return;

            window.SetBlurredDesktopBackground();
            window.Opacity = 1;
        }, DispatcherPriority.ApplicationIdle);
    }

    public void Move(int delta)
    {
        _window?.MoveSelection(delta);
    }

    public void Confirm()
    {
        var window = _window;
        _window = null;
        window?.ConfirmAndClose();
    }

    public void Hide()
    {
        HideCore();
    }

    private void HideCore()
    {
        var window = _window;
        _window = null;
        if (window is not null)
            window.Dispatcher.Invoke(() =>
            {
                window.Hide();
                window.Close();
            });
    }
}
