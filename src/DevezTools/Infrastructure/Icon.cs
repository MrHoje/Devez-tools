using System.Windows;
using System.Windows.Media;

namespace DevezTools.Infrastructure;

/// <summary>ToolButton 글자 앞에 그릴 아이콘. 버튼마다 아이콘과 글자를 한 줄로 쓰게 한다.</summary>
public static class Icon
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
        "Data", typeof(Geometry), typeof(Icon), new PropertyMetadata(null));

    public static Geometry? GetData(DependencyObject element) => (Geometry?)element.GetValue(DataProperty);
    public static void SetData(DependencyObject element, Geometry? value) => element.SetValue(DataProperty, value);
}
