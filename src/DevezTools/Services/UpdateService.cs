using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace DevezTools.Services;

/// <summary>
/// R2 의 version.json 을 보고 새 설치 파일을 받아 조용히 설치한다(배포.md·deploy.ps1 과 짝).
/// exe 옆의 OCR 네이티브 라이브러리·모델까지 함께 바뀌도록 exe 가 아니라 설치 파일로 올린다.
/// 설치 zip 은 version.json 의 sha256 과 같을 때만 쓰고, 받는 주소는 version.json 이 아니라 아래 상수로 고정한다.
/// </summary>
internal static class UpdateService
{
    private const string Base = "https://pub-37da98a9e72d4514aed3533375fb7368.r2.dev/deveztools";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "DevezTools_update");

    // csproj <Version> (x.y.z)
    public static readonly Version Current = Normalize(typeof(UpdateService).Assembly.GetName().Version!);

    public sealed record Release(Version Version, string Sha256, string Notes);

    /// <summary>새 버전이 있으면 그 정보, 없거나 확인하지 못하면 null(오프라인이면 다음 확인을 기다린다).</summary>
    public static async Task<Release?> CheckAsync()
    {
        try
        {
            // r2.dev 엣지 캐시가 옛 version.json 을 줄 수 있어 매번 다른 쿼리를 붙인다.
            Release? release = Parse(await Http.GetStringAsync($"{Base}/version.json?t={Environment.TickCount64}"));
            return release is not null && release.Version > Current ? release : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>설치 zip 을 받아(진행률 0~1) 해시를 확인하고 풀어서 설치 파일 경로를 돌려준다.</summary>
    public static async Task<string> DownloadAsync(Release release, IProgress<double> progress)
    {
        if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
        Directory.CreateDirectory(Folder);
        string zip = Path.Combine(Folder, "DevezTools_Setup.zip");
        using (HttpResponseMessage response = await Http.GetAsync($"{Base}/DevezTools_Setup.zip?v={release.Version}", HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0;
            await using Stream source = await response.Content.ReadAsStreamAsync();
            await using FileStream target = File.Create(zip);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total > 0) progress.Report((double)done / total);
            }
        }

        if (Sha256Of(zip) != release.Sha256) throw new InvalidDataException("받은 설치 파일이 배포된 파일과 달라 적용하지 않았습니다.");
        ZipFile.ExtractToDirectory(zip, Folder);
        return Directory.GetFiles(Folder, "DevezTools_Setup_*.exe").FirstOrDefault()
            ?? throw new InvalidDataException("받은 zip 에 설치 파일이 없습니다.");
    }

    /// <summary>설치 파일을 창 없이 실행한다. 설치가 끝나면 설치 파일이 앱을 --updated 로 다시 띄운다(setup.iss [Run]).</summary>
    public static void Install(string setup) => Process.Start(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART");

    /// <summary>지난 업데이트가 남긴 임시 폴더를 지운다. 아직 잡혀 있으면 다음 실행에 지운다.</summary>
    public static void CleanUp()
    {
        try { if (Directory.Exists(Folder)) Directory.Delete(Folder, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static string Sha256Of(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(file));
    }

    internal static Release? Parse(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (!Version.TryParse(root.GetProperty("version").GetString(), out Version? version)) return null;
            string sha256 = (root.GetProperty("sha256").GetString() ?? "").ToLowerInvariant();
            if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) return null;
            string notes = root.TryGetProperty("notes", out JsonElement n) ? n.GetString() ?? "" : "";
            return new Release(Normalize(version), sha256, notes);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    // 1.0.1.0 과 1.0.1 을 같게 본다(네 자리가 오면 매번 새 버전으로 보이는 일을 막는다).
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
