using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Services;

public sealed class PathService
{
    private readonly AppConfig _config;

    public PathService(IWebHostEnvironment env)
    {
        var serverRoot = env.ContentRootPath;
        var publishRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var isReleaseLocal =
            (File.Exists(Path.Combine(publishRoot, "CasePlanner.Web.Server.exe")) ||
             File.Exists(Path.Combine(publishRoot, "CasePlanner.Web.Server.dll"))) &&
            File.Exists(Path.Combine(publishRoot, "index.html"));

        var root = isReleaseLocal
            ? publishRoot
            : Directory.GetParent(Directory.GetParent(serverRoot)!.FullName)!.FullName;

        _config = new AppConfig
        {
            IsReleaseLocal = isReleaseLocal,
            RootPath = root,
            ClientDistPath = isReleaseLocal ? root : Path.Combine(root, "client", "dist"),
            DataFolder = Path.Combine(root, "data"),
            BackupsFolder = Path.Combine(root, "backups"),
            ExportsFolder = Path.Combine(root, "exports"),
            TemplatesFolder = Path.Combine(root, "templates"),
            DocumentTemplatesFolder = Path.Combine(root, "templates", "documents"),
            CustomDocumentTemplatesFolder = Path.Combine(root, "templates", "documents", "custom"),
            ReferenceFolder = Path.Combine(root, "templates", "reference"),
            LogsFolder = Path.Combine(root, "logs"),
            ImportSamplesFolder = Path.Combine(root, "import_samples"),
            DatabasePath = Path.Combine(root, "data", "case_planner_web.sqlite")
        };
    }

    public AppConfig Config => _config;

    public void EnsureFolders()
    {
        foreach (var path in new[]
                 {
                     _config.DataFolder,
                     _config.BackupsFolder,
                     _config.ExportsFolder,
                     _config.TemplatesFolder,
                     _config.DocumentTemplatesFolder,
                     _config.CustomDocumentTemplatesFolder,
                     _config.ReferenceFolder,
                     _config.LogsFolder,
                     _config.ImportSamplesFolder
                 })
        {
            Directory.CreateDirectory(path);
        }
    }

    public bool IsSafeWritableDatabase(out string message)
    {
        var db = _config.DatabasePath;
        var fullRoot = Path.GetFullPath(_config.RootPath);
        var fullDb = Path.GetFullPath(db);
        var inRoot = fullDb.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        var pythonPath = fullDb.Contains(@"Case Planner\release\CasePlannerQt_", StringComparison.OrdinalIgnoreCase);
        var wpfRelease = fullDb.Contains(@"Case Planner WPF\release\", StringComparison.OrdinalIgnoreCase);
        var writableDirs = CanWrite(_config.DataFolder) && CanWrite(_config.BackupsFolder);
        var writableDb = !File.Exists(fullDb) || CanWriteFile(fullDb);
        var ok = inRoot && !pythonPath && !wpfRelease && writableDirs && writableDb;
        message = $"path={fullDb}; inside root={inRoot}; python release={pythonPath}; wpf release={wpfRelease}; data writable={CanWrite(_config.DataFolder)}; backups writable={CanWrite(_config.BackupsFolder)}; db writable={writableDb}";
        return ok;
    }

    public static bool CanWrite(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $"write_probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool CanWriteFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            return stream.CanWrite;
        }
        catch
        {
            return false;
        }
    }
}
