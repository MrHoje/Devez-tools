using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DevezTools.Infrastructure;

/// <summary>
/// 분류 토글: 둥근 틀 안의 선택 배경(indicator)이 고른 탭으로 미끄러진다(eghisWorks Views/SegmentedTabs 이식).
/// host(Grid) 안에 indicator 와 탭(RadioButton) 을 담은 패널을 두고 Attach 한다.
/// </summary>
internal static class SegmentedTabs
{
    public static void Attach(Panel host, Panel tabs, FrameworkElement indicator)
    {
        var transform = new TranslateTransform();
        indicator.RenderTransform = transform;

        void Move(RadioButton target, bool animate)
        {
            if (!host.IsArrangeValid || target.ActualWidth <= 0)
            {
                // 아직 배치 전이면 보일 때 SizeChanged 가 맞춘다. 보이는데 폭이 0 이면 배치 뒤에 한 번만 다시 맞춘다.
                if (target.IsVisible)
                    host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { if (target.IsChecked == true && target.ActualWidth > 0) Move(target, false); });
                return;
            }

            double x = target.TransformToAncestor(host).Transform(new Point()).X;
            if (!animate || double.IsNaN(indicator.Width))
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = x;
                indicator.BeginAnimation(FrameworkElement.WidthProperty, null);
                indicator.Width = target.ActualWidth;
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(150);
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, duration) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
            indicator.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(target.ActualWidth, duration) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        }

        foreach (RadioButton tab in tabs.Children.OfType<RadioButton>())
            tab.Checked += (sender, _) => Move((RadioButton)sender, animate: true);

        host.SizeChanged += (_, _) =>
        {
            RadioButton? current = tabs.Children.OfType<RadioButton>().FirstOrDefault(t => t.IsChecked == true);
            if (current != null) Move(current, animate: false);
        };
    }
}
