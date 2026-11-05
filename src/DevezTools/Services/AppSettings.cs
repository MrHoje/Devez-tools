using System.IO;
using System.Text.Json;

namespace DevezTools.Services;

/// <summary>%APPDATA%\devezTools\settings.json 에 저장하는 사용자 설정.</summary>
public sealed class AppSettings
{
    public static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "devezTools");

    private static readonly string FilePath = Path.Combine(DataFolder, "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public string Theme { get; set; } = ThemeService.Minimal;
    // 밝기 단추로 오갈 라이트·다크 테마(오른쪽 클릭 메뉴에서 고른다)
    public string LightTheme { get; set; } = ThemeService.Minimal;
    public string DarkTheme { get; set; } = ThemeService.Charcoal;
    public string LastTool { get; set; } = "";
    // 레일 편집 결과: 도구 키 순서와 숨긴 도구
    public List<string> RailOrder { get; set; } = [];
    public List<string> HiddenTools { get; set; } = [];
    public double EditorFontSize { get; set; } = 13;
    public bool Topmost { get; set; }

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Current = new AppSettings(); // 깨진 설정은 기본값으로 시작한다
        }
    }

    public static void Save()
    {
        try
        {
            SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 설정 저장 실패는 다음 실행에 기본값으로 돌아갈 뿐이라 작업을 막지 않는다.
        }
    }
}
