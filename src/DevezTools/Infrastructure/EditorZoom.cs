using System.Windows;
using System.Windows.Input;
using DevezTools.Services;

namespace DevezTools.Infrastructure;

/// <summary>
/// 코드 편집기에서 Ctrl+휠로 글자 크기를 바꾼다. 크기는 앱 리소스 EditorFontSize 하나라서 모든 편집기가 함께 바뀌고, 다음 실행에도 유지된다.
/// </summary>
public static class EditorZoom
{
    public const double MinSize = 9;
    public const double MaxSize = 28;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(EditorZoom), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((bool)e.NewValue) element.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;
        Apply(AppSettings.Current.EditorFontSize + (e.Delta > 0 ? 1 : -1));
    }

    public static void Apply(double size)
    {
        size = Math.Clamp(size, MinSize, MaxSize);
        Application.Current.Resources["EditorFontSize"] = size;
        if (AppSettings.Current.EditorFontSize == size) return;
        AppSettings.Current.EditorFontSize = size;
        AppSettings.Save();
    }
}
