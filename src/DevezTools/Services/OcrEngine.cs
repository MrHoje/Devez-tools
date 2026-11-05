using System.IO;
using System.Net.Http;
using RapidOcrNet;
using SkiaSharp;

namespace DevezTools.Services;

/// <summary>
/// 이미지 글자 추출(talkRemind WPF 의 OcrService 이식). RapidOcrNet + PaddleOCR PP-OCRv5 한국어 인식 모델, CPU 추론.
/// 검출·방향 모델은 NuGet 이 실행 파일 옆 models\v5 에 넣어 주고, 한국어 인식 모델·사전만 처음 쓸 때 내려받는다.
/// </summary>
internal static class OcrEngine
{
    private const string ReleaseBase = "https://github.com/MrHoje/Devez-models/releases/download/v3.0/";
    private const string RecFile = "korean_PP-OCRv5_rec_mobile.onnx";
    private const string DictFile = "ppocrv5_korean_dict.txt";

    private static readonly string BundledDir = Path.Combine(AppContext.BaseDirectory, "models", "v5");
    private static readonly string ModelDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "devezTools", "Models", "v5");
    // talkRemind(Devez)가 이미 받아 둔 모델이 있으면 다시 받지 않는다
    private static readonly string SharedDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Devez", "Models", "v5");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly SemaphoreSlim Gate = new(1, 1); // 모델 준비·추론은 한 번에 하나씩

    // 한글 화면 캡처용: 검출 해상도를 키우고(작은 글자) 박스를 넓혀 받침·세로획 잘림을 줄인다.
    // 방향 분류기는 끈다 — 받침 많은 줄("읽었습니다", "닭볶음탕")을 뒤집힌 글로 오판해 그 줄이 결과에서 통째로 빠졌다(실측).
    // 대신 거꾸로 찍힌 사진은 바로잡지 않는다.
    private static readonly RapidOcrOptions Options = RapidOcrOptions.Default with { ImgResize = 2048, UnClipRatio = 2.0f, DoAngle = false };

    // 긴 변이 이보다 작은 아주 작은 캡처만 2~3배 키운다(큰 그림을 키우면 긴 줄 인식이 오히려 깨진다)
    private const int SmallSide = 480;
    private const int UpTarget = 820;

    private static RapidOcr? _ocr;

    /// <summary>PNG 바이트에서 글을 뽑는다. 처음 한 번은 모델을 내려받고 읽는다(진행 상황은 progress 로).</summary>
    public static async Task<string> ExtractAsync(byte[] png, IProgress<string> progress)
    {
        await Gate.WaitAsync();
        try
        {
            RapidOcr ocr = _ocr ??= await LoadAsync(progress);
            progress.Report("추출 중…");
            return await Task.Run(() => Recognize(ocr, png));
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<RapidOcr> LoadAsync(IProgress<string> progress)
    {
        string rec = await EnsureModelAsync(RecFile, "인식 모델", progress);
        string dict = await EnsureModelAsync(DictFile, "사전", progress);
        progress.Report("모델을 읽는 중…");
        return await Task.Run(() =>
        {
            var ocr = new RapidOcr();
            using var session = RapidOcr.GetDefaultSessionOptions();
            ocr.InitModels(Path.Combine(BundledDir, "ch_PP-OCRv5_mobile_det.onnx"),
                           Path.Combine(BundledDir, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
                           rec, dict, session);
            return ocr;
        });
    }

    private static string Recognize(RapidOcr ocr, byte[] png)
    {
        using SKBitmap decoded = SKBitmap.Decode(png) ?? throw new InvalidDataException("이미지를 읽을 수 없습니다.");
        using SKBitmap? upscaled = Upscale(decoded);
        OcrResult result = ocr.Detect(upscaled ?? decoded, Options);
        if (!string.IsNullOrWhiteSpace(result.StrRes)) return result.StrRes.Trim();
        return string.Join("\n", result.TextBlocks?.Select(b => b.Text) ?? []).Trim();
    }

    private static SKBitmap? Upscale(SKBitmap source)
    {
        int longSide = Math.Max(source.Width, source.Height);
        if (longSide <= 0 || longSide >= SmallSide) return null;
        int scale = Math.Clamp((int)Math.Round((double)UpTarget / longSide), 2, 3);
        var info = new SKImageInfo(source.Width * scale, source.Height * scale, source.ColorType, source.AlphaType);
        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
    }

    /// <summary>받아 둔 모델 경로. 없으면 내려받는다(잘린 응답·차단 페이지를 모델로 남기지 않게 크기를 확인하고 3번까지 다시 시도).</summary>
    private static async Task<string> EnsureModelAsync(string file, string label, IProgress<string> progress)
    {
        foreach (string dir in new[] { ModelDir, SharedDir })
        {
            string existing = Path.Combine(dir, file);
            if (File.Exists(existing) && new FileInfo(existing).Length > 0) return existing;
        }

        Directory.CreateDirectory(ModelDir);
        string path = Path.Combine(ModelDir, file);
        string temp = path + ".tmp";
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                progress.Report(attempt == 1 ? $"{label} 받는 중…" : $"{label} 다시 받는 중… ({attempt}/3)");
                using HttpResponseMessage response = await Http.GetAsync(ReleaseBase + file, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? -1;
                long read = 0;
                await using (Stream source = await response.Content.ReadAsStreamAsync())
                await using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[81920];
                    int count;
                    while ((count = await source.ReadAsync(buffer)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, count));
                        read += count;
                        if (total > 0) progress.Report($"{label} 받는 중… {read * 100 / total}%");
                    }
                }
                if (total > 0 && read != total) throw new IOException($"받은 크기가 맞지 않습니다({read:N0} / {total:N0}바이트).");
                if (read < 1024) throw new IOException($"받은 파일이 너무 작습니다({read:N0}바이트).");
                File.Move(temp, path, overwrite: true);
                return path;
            }
            catch (Exception ex) when ((ex is HttpRequestException or IOException or TaskCanceledException) && attempt < 3)
            {
                File.Delete(temp);
                await Task.Delay(800 * attempt);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                File.Delete(temp);
                throw new IOException($"{label}을(를) 받지 못했습니다: {ex.Message}", ex);
            }
        }
    }
}
