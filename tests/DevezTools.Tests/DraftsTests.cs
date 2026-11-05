using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Documents;
using DevezTools.Infrastructure;

namespace DevezTools.Tests;

public class DraftsTests
{
    private static StackPanel View() => new()
    {
        Children =
        {
            new Border { Child = new TextBox { Name = "Input" } }, // 안쪽 칸도 찾는다
            new TextBox { Name = "Output", IsReadOnly = true },
            new TextBox(),                                         // 이름 없는 칸은 담지 않는다
            new CheckBox { Name = "Option" },
            new ComboBox { Name = "Pick", Items = { "a", "b" } },
            new RichTextBox { Name = "Token" },
        },
    };

    [Fact]
    public void Restores_named_inputs_and_skips_read_only() => Sta(() =>
    {
        StackPanel before = View();
        ((TextBox)((Border)before.Children[0]).Child).Text = "입력한 글";
        ((TextBox)before.Children[1]).Text = "결과";
        ((CheckBox)before.Children[3]).IsChecked = true;
        ((ComboBox)before.Children[4]).SelectedIndex = 1;
        RichTextBox token = (RichTextBox)before.Children[5];
        new TextRange(token.Document.ContentStart, token.Document.ContentEnd).Text = "eyJ.abc";
        Drafts.Capture("drafts-test", before);

        StackPanel after = View();
        Drafts.Restore("drafts-test", after);

        Assert.Equal("입력한 글", ((TextBox)((Border)after.Children[0]).Child).Text);
        Assert.Equal("", ((TextBox)after.Children[1]).Text);
        Assert.True(((CheckBox)after.Children[3]).IsChecked);
        Assert.Equal(1, ((ComboBox)after.Children[4]).SelectedIndex);
        RichTextBox restored = (RichTextBox)after.Children[5];
        Assert.Equal("eyJ.abc", new TextRange(restored.Document.ContentStart, restored.Document.ContentEnd).Text.TrimEnd());
    });

    // WPF 컨트롤은 STA 스레드에서만 만들 수 있다
    private static void Sta(Action test)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }
}
