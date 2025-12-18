namespace OlivePetrel;

public sealed class LinePrinter
{
    private StreamWriter? _writer;
    private string? _path;
    private bool _reportedError;

    public bool Attached => _writer is not null;
    public string? Path => _path;

    public bool Attach(string path, out string? error)
    {
        try
        {
            Detach();
            var fullPath = System.IO.Path.GetFullPath(path);
            var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream) { AutoFlush = true };
            _path = fullPath;
            _reportedError = false;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            Detach();
            error = $"Attach failed: {ex.Message}";
            return false;
        }
    }

    public void Detach()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // ignore dispose errors
        }
        finally
        {
            _writer = null;
            _path = null;
            _reportedError = false;
        }
    }

    public void Write(char ch)
    {
        if (_writer is null)
        {
            return;
        }

        try
        {
            _writer.Write(ch);
        }
        catch (Exception ex)
        {
            if (!_reportedError)
            {
                Console.WriteLine($"Line printer write failed: {ex.Message}");
                _reportedError = true;
            }
        }
    }
}
