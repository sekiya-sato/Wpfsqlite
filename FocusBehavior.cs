using System.Windows;
using System.Windows.Input;

namespace Wpfsqlite;

public static class FocusBehavior {
    public static readonly DependencyProperty NotifyOnFocusProperty = DependencyProperty.RegisterAttached(
        "NotifyOnFocus",
        typeof(bool),
        typeof(FocusBehavior),
        new PropertyMetadata(false, OnNotifyOnFocusChanged));

    public static void SetNotifyOnFocus(DependencyObject element, bool value) => element.SetValue(NotifyOnFocusProperty, value);
    public static bool GetNotifyOnFocus(DependencyObject element) => (bool)element.GetValue(NotifyOnFocusProperty);

    private static void OnNotifyOnFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is UIElement ui) {
            if ((bool)e.NewValue) {
                ui.GotKeyboardFocus += Ui_GotKeyboardFocus;
            }
            else {
                ui.GotKeyboardFocus -= Ui_GotKeyboardFocus;
            }
        }
    }

    private static void Ui_GotKeyboardFocus(object? sender, KeyboardFocusChangedEventArgs e) {
        if (sender is FrameworkElement fe) {
            // DataContext of the focused element in the DataTemplate should be ColumnInfo
            if (fe.DataContext is ViewModels.ColumnInfo col) {
                var window = Window.GetWindow(fe);
                if (window?.DataContext is ViewModels.MainViewModel vm) {
                    vm.SelectedColumn = col;
                }
            }
        }
    }
}
