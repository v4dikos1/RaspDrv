using System.Runtime.InteropServices;
using System.Text;

namespace AutoGraph;

public class SymlinkResolver
{
    // P/Invoke для системного вызова readlink
    [DllImport("libc", SetLastError = true)]
    private static extern int readlink(string path, byte[] buf, int bufSize);

    // Получение реального пути из символьной ссылки
    public static string GetRealPath(string symlinkPath)
    {
        const int maxPathLength = 4096; // Обычная длина пути в Linux
        byte[] buffer = new byte[maxPathLength];

        int length = readlink(symlinkPath, buffer, buffer.Length);

        if (length == -1)
        {
            int errorCode = Marshal.GetLastWin32Error();
            Console.WriteLine($"Error: Unable to read link. Error code: {errorCode}");
            return null;
        }

        string relativePath = Encoding.UTF8.GetString(buffer, 0, length).Trim();
        string symlinkDirectory = Path.GetDirectoryName(symlinkPath);

        string absolutePath = Path.GetFullPath(Path.Combine(symlinkDirectory, relativePath));

        return absolutePath;
    }
}