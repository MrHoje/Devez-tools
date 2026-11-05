using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DevezTools.Infrastructure;
using DevezTools.Services;

namespace DevezTools.Views;

public partial class ClipboardView : UserControl
{
    public ClipboardView()
    {
        InitializeComponent();
        ClipList.ItemsSource = ClipboardHistory.Items;
        ClipboardHistory.Items.CollectionChanged += Items_CollectionChanged;
        // 다시 볼 때 "n분 전"을 새로 계산한다
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            foreach (ClipboardItem item in ClipboardHistory.Items) item.RefreshMeta();
            ShowSelected();
        };
        UpdateState();
    }

    private ClipboardItem? Selected => ClipList.SelectedItem as ClipboardItem;

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateState();

    private void UpdateState()
    {
        int count = ClipboardHistory.Items.Count;
        CountText.Text = count > 0 ? $"{count}개" : "";
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowSelected();
    }

    private void ClipList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSelected();

    private void ShowSelected()
    {
        ClipboardItem? item = Selected;
        DetailText.Text = item?.Text ?? "";
        DetailMeta.Text = item?.Meta ?? "";
        CopyButton.IsEnabled = item is not null;
    }

    private void ClipItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBoxItem)?.DataContext is ClipboardItem item) Ui.Copy(item.Text);
    }

    private void ClipList_KeyDown(object sender, KeyEventArgs e)
    {
        if (Selected is not { } item) return;
        if (e.Key == Key.Enter) Ui.Copy(item.Text);
        else if (e.Key == Key.Delete) ClipboardHistory.Remove(item);
        else return;
        e.Handled = true;
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => Ui.Copy(Selected?.Text);

    private void CopyItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardItem item) Ui.Copy(item.Text);
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardItem item) ClipboardHistory.Remove(item);
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e) => ClipboardHistory.Clear();
}
