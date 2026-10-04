using System.Text;

namespace ShartyTools.Core.IO;

public static class AtomicFile
{
    public static void Write(string path, Action<Stream> write, bool overwrite = false)
    {
        path = Path.GetFullPath(path);
        GamePath.RejectLinks(path);
        var parent = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException($"Output directory does not exist: {parent}");
        if (File.Exists(path) && !overwrite)
            throw new IOException($"Output already exists: {path}. Choose a new filename.");
        var temporary = Path.Combine(parent, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(true);
            }
            GamePath.RejectLinks(path);
            File.Move(temporary, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void WriteText(string path, string text, bool overwrite = false) =>
        Write(path, stream => stream.Write(Encoding.UTF8.GetBytes(text)), overwrite);
}
