using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DevezTools.Infrastructure;

namespace DevezTools.Views;

public partial class HashView : UserControl
{
    private sealed record HashRow(string Name, string Value);

    private static readonly (string Name, HashAlgorithmName Algorithm)[] Algorithms =
    [
        ("MD5", HashAlgorithmName.MD5),
        ("SHA-1", HashAlgorithmName.SHA1),
        ("SHA-256", HashAlgorithmName.SHA256),
        ("SHA-512", HashAlgorithmName.SHA512),
    ];

    private readonly Debouncer _recompute;
    private Guid _guid = Guid.NewGuid();
    private int _hashVersion; // 새 입력·새 파일이 오면 늦게 끝난 파일 계산은 버린다

    public HashView()
    {
        InitializeComponent();
        _recompute = new Debouncer(150, Recompute);
        Recompute();
        ShowGuid();
    }

    private void InputText_TextChanged(object sender, TextChangedEventArgs e)
    {
        InputMeta.Text = Ui.Stats(InputText.Text);
        _recompute.Kick();
    }

    private void Recompute()
    {
        string text = InputText.Text;
        EncodedText.Text = Uri.EscapeDataString(text);
        DecodedText.Text = WebUtility.UrlDecode(text);

        _hashVersion++;
        HashMeta.Text = "UTF-8 바이트 기준";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        HashList.ItemsSource = Algorithms
            .Select(a => new HashRow(a.Name, text.Length == 0 ? "" : Convert.ToHexStringLower(CryptographicOperations.HashData(a.Algorithm, bytes))))
            .ToArray();
    }

    // ── 파일 해시 ──

    private async Task HashFileAsync(string path)
    {
        if (Directory.Exists(path))
        {
            Ui.Toast("폴더는 해시를 구할 수 없습니다. 파일을 놓아 주십시오.");
            return;
        }

        int version = ++_hashVersion;
        string name = Path.GetFileName(path);
        HashMeta.Text = $"{name} 계산 중…";
        HashList.ItemsSource = Algorithms.Select(a => new HashRow(a.Name, "")).ToArray();
        try
        {
            (HashRow[] rows, long size) = await Task.Run(() => ComputeFile(path));
            if (version != _hashVersion) return;
            HashList.ItemsSource = rows;
            HashMeta.Text = $"{name} · {RestView.FormatSize(size)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (version != _hashVersion) return;
            HashMeta.Text = "파일을 읽지 못했습니다";
            Ui.Toast("파일을 읽지 못했습니다: " + ex.Message);
        }
    }

    // 큰 파일도 한 번만 읽으며 네 가지 해시를 함께 구한다
    private static (HashRow[] Rows, long Size) ComputeFile(string path)
    {
        IncrementalHash[] hashes = Algorithms.Select(a => IncrementalHash.CreateHash(a.Algorithm)).ToArray();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            byte[] buffer = new byte[1 << 20];
            long size = 0;
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                size += read;
                foreach (IncrementalHash hash in hashes) hash.AppendData(buffer, 0, read);
            }
            return (Algorithms.Select((a, i) => new HashRow(a.Name, Convert.ToHexStringLower(hashes[i].GetHashAndReset()))).ToArray(), size);
        }
        finally
        {
            foreach (IncrementalHash hash in hashes) hash.Dispose();
        }
    }

    private void View_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return; // 글자 끌기는 입력칸이 처리한다
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void View_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        e.Handled = true;
        await HashFileAsync(files[0]);
    }

    private async void PickFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "해시를 구할 파일" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await HashFileAsync(dialog.FileName);
    }

    // ── GUID ──

    private void ShowGuid()
    {
        string guid = _guid.ToString(NoHyphenOption.IsChecked == true ? "N" : "D");
        GuidText.Text = UpperOption.IsChecked == true ? guid.ToUpperInvariant() : guid;
    }

    private void NewGuid_Click(object sender, RoutedEventArgs e)
    {
        _guid = Guid.NewGuid();
        ShowGuid();
    }

    // 형식만 바꾸고 값은 그대로 둔다
    private void GuidOption_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized) ShowGuid();
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (Ui.PasteText() is { } text) InputText.Text = text;
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => InputText.Clear();
    private void CopyEncoded_Click(object sender, RoutedEventArgs e) => Ui.Copy(EncodedText.Text);
    private void CopyDecoded_Click(object sender, RoutedEventArgs e) => Ui.Copy(DecodedText.Text);
    private void CopyGuid_Click(object sender, RoutedEventArgs e) => Ui.Copy(GuidText.Text);
    private void CopyHash_Click(object sender, RoutedEventArgs e) => Ui.Copy(((sender as FrameworkElement)?.DataContext as HashRow)?.Value);
}
