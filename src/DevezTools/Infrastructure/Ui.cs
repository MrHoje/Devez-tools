using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DevezTools.Views;

namespace DevezTools.Infrastructure;

/// <summary>클립보드와 알림처럼 여러 도구가 함께 쓰는 화면 동작.</summary>
internal static class Ui
{
    public static void Toast(string message) => (Application.Current?.MainWindow as MainWindow)?.ShowToast(message);

    /// <summary>카드 머리글에 보이는 "12줄 · 340자". 비었으면 빈 글자.</summary>
    public static string Stats(string text)
    {
        if (text.Length == 0) return "";
        int lines = 1;
        foreach (char c in text) if (c == '\n') lines++;
        return $"{lines:N0}줄 · {text.Length:N0}자";
    }

    /// <summary>클립보드의 글자. 비었거나 다른 프로그램이 잡고 있으면 null.</summary>
    public static string? PasteText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (COMException)
        {
            Toast("클립보드를 읽지 못했습니다. 잠시 뒤 다시 시도해 주십시오.");
            return null;
        }
    }

    public static void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        // 다른 프로그램이 클립보드를 잠깐 잡고 있으면 실패하므로 몇 번 다시 해 본다.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                Toast("복사되었습니다");
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
        }
        Toast("클립보드를 사용할 수 없습니다. 잠시 뒤 다시 시도해 주십시오.");
    }
}

/// <summary>입력이 멈춘 뒤 한 번만 실행한다(타자 칠 때마다 다시 계산하지 않게).</summary>
internal sealed class Debouncer
{
    private readonly DispatcherTimer _timer;
    private readonly Action _action;

    public Debouncer(int milliseconds, Action action)
    {
        _action = action;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _action();
        };
    }

    public void Kick()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Flush()
    {
        _timer.Stop();
        _action();
    }
}
