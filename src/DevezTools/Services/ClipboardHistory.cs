using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;

namespace DevezTools.Services;

/// <summary>클립보드 기록 한 건. 저장은 Text·CopiedAt 만 한다.</summary>
public sealed class ClipboardItem : INotifyPropertyChanged
{
    private DateTime _copiedAt;

    public string Text { get; set; } = "";

    public DateTime CopiedAt
    {
        get => _copiedAt;
        set
        {
            _copiedAt = value;
            RefreshMeta();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>"n분 전" 표시는 시간이 지나면 낡으므로 화면을 다시 열 때 새로 그린다.</summary>
    public void RefreshMeta() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Meta)));

    /// <summary>목록용 한 줄 미리보기(줄바꿈·연속 공백 정리, 200자까지).</summary>
    [JsonIgnore]
    public string Preview
    {
        get
        {
            string text = string.Join(' ', Text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            return text.Length > 200 ? text[..200] + "…" : text;
        }
    }

    [JsonIgnore]
    public string Meta
    {
        get
        {
            TimeSpan span = DateTime.Now - CopiedAt;
            string when = span < TimeSpan.FromMinutes(1) ? "방금 전"
                : span < TimeSpan.FromHours(1) ? $"{(int)span.TotalMinutes}분 전"
                : span < TimeSpan.FromDays(1) ? $"{(int)span.TotalHours}시간 전"
                : span < TimeSpan.FromDays(2) ? "어제"
                : $"{(int)span.TotalDays}일 전";
            return $"{when} · {Text.Length:N0}자";
        }
    }
}

/// <summary>
/// 다른 프로그램에서 복사한 글을 모아 둔다(talkRemind WPF 의 ClipboardHistoryService 에서 DB 동기화를 뺀 것).
/// 최대 50개·7일, %APPDATA%\devezTools\clipboard.json. 항목은 UI 스레드에서만 다룬다(WM_CLIPBOARDUPDATE 는 메인 창으로 온다).
/// </summary>
internal static class ClipboardHistory
{
    private const int MaxItems = 50;
    private const int MaxTextLength = 50_000;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private const int WmClipboardUpdate = 0x031D;
    private static readonly string FilePath = Path.Combine(AppSettings.DataFolder, "clipboard.json");

    /// <summary>최신 항목이 맨 앞.</summary>
    public static ObservableCollection<ClipboardItem> Items { get; } = [];

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    /// <summary>저장한 기록을 읽고 창으로 오는 클립보드 변경 알림을 받기 시작한다. 창이 숨어 있어도 기록한다.</summary>
    public static void Start(Window window)
    {
        Load();
        IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
        HwndSource.FromHwnd(handle).AddHook((IntPtr _, int message, IntPtr _, IntPtr _, ref bool _) =>
        {
            if (message == WmClipboardUpdate) Capture();
            return IntPtr.Zero;
        });
        AddClipboardFormatListener(handle);
    }

    private static async void Capture()
    {
        await Task.Delay(80); // 복사한 프로그램이 클립보드를 다 쓰고 닫을 틈을 준다
        try
        {
            IDataObject? data = Clipboard.GetDataObject();
            if (data is null || IsExcluded(data) || data.GetData(DataFormats.UnicodeText) is not string text) return;
            Add(text);
        }
        catch (Exception ex) when (ex is ExternalException or OutOfMemoryException)
        {
            // 다른 프로그램이 클립보드를 잡고 있으면 이번 복사는 건너뛴다.
        }
    }

    // 비밀번호 관리자 등이 "기록하지 말라"고 표시한 내용은 남기지 않는다(Windows 클립보드 기록과 같은 규칙).
    private static bool IsExcluded(IDataObject data)
    {
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore")) return true;
        return data.GetData("CanIncludeInClipboardHistory") is MemoryStream flag && flag.Length >= 4 && BitConverter.ToInt32(flag.ToArray(), 0) == 0;
    }

    /// <summary>같은 글이 있으면 그 항목을 맨 앞으로 올린다(선택 유지).</summary>
    public static void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength) return;

        int existing = -1;
        for (int i = 0; i < Items.Count && existing < 0; i++)
            if (Items[i].Text == text) existing = i;

        if (existing >= 0)
        {
            Items[existing].CopiedAt = DateTime.Now;
            if (existing > 0) Items.Move(existing, 0);
        }
        else
        {
            Items.Insert(0, new ClipboardItem { Text = text, CopiedAt = DateTime.Now });
        }
        Prune();
        Save();
    }

    public static void Remove(ClipboardItem item)
    {
        if (Items.Remove(item)) Save();
    }

    public static void Clear()
    {
        Items.Clear();
        Save();
    }

    private static void Prune()
    {
        DateTime cutoff = DateTime.Now - MaxAge;
        for (int i = Items.Count - 1; i >= 0; i--)
            if (i >= MaxItems || Items[i].CopiedAt < cutoff) Items.RemoveAt(i);
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (ClipboardItem item in JsonSerializer.Deserialize<List<ClipboardItem>>(File.ReadAllText(FilePath)) ?? []) Items.Add(item);
            Prune();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 깨진 기록은 버리고 빈 기록으로 시작한다(다음 복사 때 새로 저장된다).
        }
    }

    private static void Save()
    {
        try
        {
            SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(Items));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 저장 실패는 이번 실행 동안의 기록만 남을 뿐이라 작업을 막지 않는다.
        }
    }
}
