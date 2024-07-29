using System.Runtime.InteropServices;
using System.Text;

namespace RaspDrv.Com.Helpers;

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

    // FileSystemWatcher при считывании файлов, являющихся символьными ссылками
    // (которыми являются в том числе и записи о подключенных метках), добавляет в начало имени
    // файла .# и в конец имени суффикс из 16 символов.
    // Для дальнейшей работы с файлом ненужные символы обрезаются
    public static string TrimTempSymbols(string str, int positionFromEnd)
    {
        if (string.IsNullOrEmpty(str) || str.Length < positionFromEnd)
        {
            return str;
        }

        if (str.Contains(".#"))
        {
            str = str.Replace(".#", string.Empty);
            var indexFromEnd = positionFromEnd - 1;
            var index = str.Length - 1 - indexFromEnd;

            if (index >= 0)
            {
                return str.Substring(0, index);
            }
        }

        return str;
    }
}