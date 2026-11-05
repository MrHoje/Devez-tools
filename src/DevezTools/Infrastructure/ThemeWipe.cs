using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace DevezTools.Infrastructure;

/// <summary>eghisWorks Motion.DiagonalWipe 를 옮겼다: 테마를 바꿀 때 이전 화면을 대각선으로 걷어내 새 화면을 드러낸다.</summary>
internal static class ThemeWipe
{
    /// <summary>전환이 진행 중이면 참. 연달아 누르면 겹쳐 끊기므로 끝날 때까지 새 전환을 받지 않는다.</summary>
    public static bool IsWiping { get; private set; }

    /// <summary>바꾸기 전 화면을 찍어 덮고 change 를 실행한 뒤, 오른쪽 위에서 왼쪽 아래로(reverse 면 반대로) 걷어낸다.</summary>
    public static void Run(Panel host, Action change, bool reverse = false)
    {
        double width = host.ActualWidth, height = host.ActualHeight;
        Window? window = Window.GetWindow(host);
        if (!SystemParameters.ClientAreaAnimation || window == null || width < 1 || height < 1) { change(); return; }
        DpiScale dpi = VisualTreeHelper.GetDpi(host);
        // 최대화하면 host 에 창 테두리만큼 여백이 붙어, host 를 바로 찍으면 그만큼 밀린다.
        // 위치가 늘 0인 창 전체를 찍고 host 자리만 장치 픽셀 단위로 잘라 쓴다.
        var full = new RenderTargetBitmap(
            (int)Math.Round(window.ActualWidth * dpi.DpiScaleX), (int)Math.Round(window.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        full.Render(window);
        Point offset = host.TransformToAncestor(window).Transform(new Point());
        int left = Math.Clamp((int)Math.Round(offset.X * dpi.DpiScaleX), 0, full.PixelWidth - 1);
        int top = Math.Clamp((int)Math.Round(offset.Y * dpi.DpiScaleY), 0, full.PixelHeight - 1);
        var shot = new CroppedBitmap(full, new Int32Rect(left, top,
            Math.Min((int)Math.Round(width * dpi.DpiScaleX), full.PixelWidth - left), Math.Min((int)Math.Round(height * dpi.DpiScaleY), full.PixelHeight - top)));

        // 가림막(이전 화면)에 대각선 그라데이션 마스크를 씌우고 그 경계를 옮긴다. 경계 앞 band 폭만큼 흐리게 사라진다.
        const double band = 160;
        var along = new Vector(width, height);
        along.Normalize();
        Vector toCover = reverse ? new Vector(along.Y, -along.X) : new Vector(-along.Y, along.X);
        Point origin = reverse ? new Point(0, height) : new Point(width, 0);
        var move = new TranslateTransform();
        var cover = new Image
        {
            Source = shot,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            OpacityMask = new LinearGradientBrush(Colors.Transparent, Colors.Black, origin - toCover * band, origin) { MappingMode = BrushMappingMode.Absolute, Transform = move },
        };
        Panel.SetZIndex(cover, int.MaxValue);
        // 경계가 움직일 때마다 아래 화면을 다시 그리지 않게, 지나가는 동안만 새 테마로 한 번 그린 그림을 쓴다.
        Action uncache = CacheWhileMoving(host.Children.OfType<UIElement>(), CacheScale(host));
        host.Children.Add(cover);
        IsWiping = true;
        change();
        // 흐림 띠까지 반대쪽 모서리를 다 지나가도록 띠 폭만큼 더 민다.
        double travel = 1 + band * Math.Sqrt(width * width + height * height) / (2 * width * height);

        // 테마 적용으로 화면이 잠시 멈추는 동안 시간이 흘러 효과가 건너뛰지 않게, 새 화면을 처음 그릴 때 시작한다.
        EventHandler? start = null;
        start = (_, _) =>
        {
            CompositionTarget.Rendering -= start;
            TimeSpan duration = TimeSpan.FromMilliseconds(400);
            IEasingFunction ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var sweepX = new DoubleAnimation(0, (reverse ? width : -width) * travel, duration) { EasingFunction = ease };
            var sweepY = new DoubleAnimation(0, (reverse ? -height : height) * travel, duration) { EasingFunction = ease };
            Timeline.SetDesiredFrameRate(sweepX, 144);
            Timeline.SetDesiredFrameRate(sweepY, 144);
            sweepX.Completed += (_, _) =>
            {
                host.Children.Remove(cover);
                uncache();
                IsWiping = false;
            };
            move.BeginAnimation(TranslateTransform.XProperty, sweepX);
            move.BeginAnimation(TranslateTransform.YProperty, sweepY);
        };
        CompositionTarget.Rendering += start;
    }

    /// <summary>BitmapCache 를 화면 픽셀과 1:1로 그리는 배율(WPF 가 캐시에 이미 곱하는 주 모니터 배율로 나눈다).</summary>
    private static double CacheScale(Visual visual)
    {
        double zoom = 1;
        if (PresentationSource.FromVisual(visual)?.RootVisual is Visual root && root != visual)
        {
            GeneralTransform toRoot = visual.TransformToAncestor(root);
            zoom = (toRoot.Transform(new Point(1, 0)) - toRoot.Transform(new Point())).Length;
        }
        return zoom * VisualTreeHelper.GetDpi(visual).PixelsPerInchX / GetDpiForSystem();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private static Action CacheWhileMoving(IEnumerable<UIElement> elements, double scale)
    {
        var cached = new List<(UIElement Element, BitmapCache Cache)>();
        foreach (UIElement element in elements)
        {
            if (element.CacheMode != null) continue;
            var cache = new BitmapCache(scale) { SnapsToDevicePixels = true };
            element.CacheMode = cache;
            cached.Add((element, cache));
        }
        return () =>
        {
            foreach ((UIElement element, BitmapCache cache) in cached)
                if (element.CacheMode == cache) element.CacheMode = null;
        };
    }
}
