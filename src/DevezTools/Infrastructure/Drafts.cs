using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using DevezTools.Services;

namespace DevezTools.Infrastructure;

/// <summary>
/// 도구 화면에 입력한 글·선택을 %APPDATA%\devezTools\drafts.json 에 담아 다시 실행해도 이어 쓰게 한다.
/// 이름(x:Name)이 붙은 편집 칸·확인란·단추 탭·콤보 상자를 "도구.이름" 키로 담는다.
/// 읽기 전용 결과 칸은 입력에서 다시 계산되므로 담지 않는다. 목록 템플릿 안의 칸(REST 헤더 줄 등)은 화면이 Get/Set 으로 직접 맡긴다.
/// </summary>
internal static class Drafts
{
    private static readonly string FilePath = Path.Combine(AppSettings.DataFolder, "drafts.json");
    private static readonly Dictionary<string, string> Values = Load();

    public static string? Get(string key) => Values.GetValueOrDefault(key);

    public static void Set(string key, string value) => Values[key] = value;

    public static void Restore(string tool, DependencyObject view)
    {
        foreach (FrameworkElement control in Editable(view))
        {
            if (!Values.TryGetValue($"{tool}.{control.Name}", out string? value)) continue;
            switch (control)
            {
                case TextBox box: box.Text = value; break;
                case RichTextBox rich: new TextRange(rich.Document.ContentStart, rich.Document.ContentEnd).Text = value; break;
                case ToggleButton toggle: toggle.IsChecked = value == bool.TrueString; break;
                case ComboBox combo when int.TryParse(value, out int index) && index < combo.Items.Count: combo.SelectedIndex = index; break;
            }
        }
    }

    public static void Capture(string tool, DependencyObject view)
    {
        foreach (FrameworkElement control in Editable(view))
        {
            Values[$"{tool}.{control.Name}"] = control switch
            {
                TextBox box => box.Text,
                RichTextBox rich => new TextRange(rich.Document.ContentStart, rich.Document.ContentEnd).Text.TrimEnd(),
                ToggleButton toggle => (toggle.IsChecked == true).ToString(),
                _ => ((ComboBox)control).SelectedIndex.ToString(),
            };
        }
    }

    /// <summary>글을 고치거나 선택을 바꾸면 잠시 뒤 save 를 부른다(강제 종료돼도 마지막 입력이 남게).</summary>
    public static void Watch(UIElement view, Action save)
    {
        var debouncer = new Debouncer(800, save);
        view.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => debouncer.Kick()));
        view.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => debouncer.Kick()));
        view.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => debouncer.Kick()));
        view.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => debouncer.Kick()));
    }

    public static void Save()
    {
        try
        {
            SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(Values));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 저장 실패는 다음 실행에 지난 입력이 비어 보일 뿐이라 작업을 막지 않는다.
        }
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return [];
    }

    // 논리 트리에서 이름 붙은 편집 컨트롤만 고른다(템플릿 안쪽은 논리 트리에 없다)
    private static IEnumerable<FrameworkElement> Editable(DependencyObject node)
    {
        foreach (DependencyObject child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            if (child is FrameworkElement { Name.Length: > 0 } element
                && element is TextBoxBase { IsReadOnly: false } or ToggleButton or ComboBox)
                yield return element;
            foreach (FrameworkElement inner in Editable(child)) yield return inner;
        }
    }
}
