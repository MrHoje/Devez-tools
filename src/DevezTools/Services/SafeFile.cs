using System.IO;

namespace DevezTools.Services;

internal static class SafeFile
{
    /// <summary>임시 파일에 다 쓴 뒤 바꿔치기한다. 쓰는 도중 꺼져도 원래 파일이 잘린 채 남지 않는다.</summary>
    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
