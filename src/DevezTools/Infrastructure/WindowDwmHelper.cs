using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DevezTools.Infrastructure;

/// <summary>
/// 불투명 보더리스 창의 모서리를 Windows 11 DWM 라운드로 처리한다(eghisWorks WindowDwmHelper 와 같은 방식).
/// AllowsTransparency + CornerRadius 로 흉내 내지 않는다.
/// </summary>
internal static class WindowDwmHelper
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpDoNotRound = 1;
    private const int DwmwcpRound = 2;
    private const int GwlStyle = -16;
    private const int WsCaption = 0x00C00000;
    private const int SmCxFrame = 32;
    private const int SmCyFrame = 33;
    private const int SmCxPaddedBorder = 92;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    /// <summary>SourceInitialized 에서 한 번 부른다. 캡션 스타일을 되살려 최대화·복원 애니메이션이 동작하게 한다.</summary>
    public static void Initialize(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        ApplyCornerPreference(window);
        SetWindowLong(hwnd, GwlStyle, GetWindowLong(hwnd, GwlStyle) | WsCaption);
    }

    /// <summary>일반=둥근 모서리, 최대화=각진 모서리(화면 모서리가 깎이지 않게).</summary>
    public static void ApplyCornerPreference(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int preference = window.WindowState == WindowState.Maximized ? DwmwcpDoNotRound : DwmwcpRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    /// <summary>캡션+두꺼운 프레임 창을 최대화하면 프레임 두께만큼 내용이 화면 밖으로 잘려 그만큼 안쪽 여백을 준다.</summary>
    public static void ApplyMaximizeMargin(Window window, FrameworkElement root)
    {
        if (window.WindowState != WindowState.Maximized)
        {
            root.Margin = new Thickness(0);
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        uint windowDpi = (uint)Math.Round(96 * dpi.DpiScaleX);
        int pad = GetSystemMetricsForDpi(SmCxPaddedBorder, windowDpi);
        double x = (GetSystemMetricsForDpi(SmCxFrame, windowDpi) + pad) / dpi.DpiScaleX;
        double y = (GetSystemMetricsForDpi(SmCyFrame, windowDpi) + pad) / dpi.DpiScaleY;
        root.Margin = new Thickness(x, y, x, y);
    }
}
