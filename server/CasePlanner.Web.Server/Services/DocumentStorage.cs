using System.Text;

namespace CasePlanner.Web.Server.Services;

public sealed class DocumentStorageOptions
{
    public const string SectionName="DocumentStorage";
    public string Provider { get; set; }="FileSystem";
    public string? RootPath { get; set; }
}

public interface IDocumentStorage
{
    string Provider { get; }
    string RootPath { get; }
    string CreatePath(long caseId,string fileName);
    Task WriteTextAsync(string path,string content,CancellationToken token=default);
    Task WriteLinesAsync(string path,IEnumerable<string> lines,CancellationToken token=default);
    Task WriteBytesAsync(string path,byte[] content,CancellationToken token=default);
    Task<string?> ReadTextAsync(string path,CancellationToken token=default);
    Task<Stream?> OpenReadAsync(string path,CancellationToken token=default);
    Task DeleteIfExistsAsync(string path,CancellationToken token=default);
}

public interface ITemplateFileStorage
{
    string Provider { get; }
    string RootPath { get; }
    string CreatePath(string baseKey,string fileName);
    Task WriteBytesAsync(string path,byte[] content,CancellationToken token=default);
    Task<byte[]?> ReadBytesAsync(string path,CancellationToken token=default);
    Task<string?> ReadTextAsync(string path,CancellationToken token=default);
    Task<Stream?> OpenReadAsync(string path,CancellationToken token=default);
    Task DeleteIfExistsAsync(string path,CancellationToken token=default);
}

public sealed class FileSystemDocumentStorage:IDocumentStorage
{
    private readonly string _rootWithSeparator;
    public string Provider=>"FileSystem";
    public string RootPath { get; }

    public FileSystemDocumentStorage(PathService paths,DocumentStorageOptions? options=null)
    {
        if(options is not null&&!string.Equals(options.Provider,"FileSystem",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported document storage provider '{options.Provider}'. This build supports FileSystem only.");
        RootPath=Path.GetFullPath(string.IsNullOrWhiteSpace(options?.RootPath)?paths.Config.ExportsFolder:options.RootPath);
        _rootWithSeparator=RootPath.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
    }

    public string CreatePath(long caseId,string fileName)
    {
        if(caseId<=0)throw new ArgumentOutOfRangeException(nameof(caseId));
        if(string.IsNullOrWhiteSpace(fileName)||fileName!=Path.GetFileName(fileName))throw new ArgumentException("A plain document file name is required.",nameof(fileName));
        var path=Path.GetFullPath(Path.Combine(RootPath,"cases",caseId.ToString(),fileName));
        EnsureManaged(path);return path;
    }

    public async Task WriteTextAsync(string path,string content,CancellationToken token=default)
    {
        EnsureParent(path);await File.WriteAllTextAsync(path,content,Encoding.UTF8,token);
    }
    public async Task WriteLinesAsync(string path,IEnumerable<string> lines,CancellationToken token=default)
    {
        EnsureParent(path);await File.WriteAllLinesAsync(path,lines,Encoding.UTF8,token);
    }
    public async Task WriteBytesAsync(string path,byte[] content,CancellationToken token=default)
    {
        EnsureParent(path);await File.WriteAllBytesAsync(path,content,token);
    }
    public async Task<string?> ReadTextAsync(string path,CancellationToken token=default)
    {
        EnsureManaged(path);return File.Exists(path)?await File.ReadAllTextAsync(path,token):null;
    }
    public Task<Stream?> OpenReadAsync(string path,CancellationToken token=default)
    {
        EnsureManaged(path);Stream? stream=File.Exists(path)?new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read):null;return Task.FromResult(stream);
    }
    public Task DeleteIfExistsAsync(string path,CancellationToken token=default)
    {
        EnsureManaged(path);if(File.Exists(path))File.Delete(path);return Task.CompletedTask;
    }
    private void EnsureParent(string path){EnsureManaged(path);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);}
    private void EnsureManaged(string path)
    {
        var full=Path.GetFullPath(path);
        if(!full.StartsWith(_rootWithSeparator,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("The document path is outside the configured storage root.");
    }
}

public sealed class FileSystemTemplateStorage:ITemplateFileStorage
{
    private readonly string _rootWithSeparator;
    public string Provider=>"FileSystem";
    public string RootPath { get; }

    public FileSystemTemplateStorage(IDocumentStorage documents)
    {
        RootPath=Path.GetFullPath(Path.Combine(documents.RootPath,"templates","custom"));
        _rootWithSeparator=RootPath.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
    }

    public string CreatePath(string baseKey,string fileName)
    {
        if(string.IsNullOrWhiteSpace(baseKey)||baseKey!=Path.GetFileName(baseKey))throw new ArgumentException("A plain template base key is required.",nameof(baseKey));
        if(string.IsNullOrWhiteSpace(fileName)||fileName!=Path.GetFileName(fileName))throw new ArgumentException("A plain template file name is required.",nameof(fileName));
        var path=Path.GetFullPath(Path.Combine(RootPath,baseKey,fileName));EnsureManaged(path);return path;
    }
    public async Task WriteBytesAsync(string path,byte[] content,CancellationToken token=default){EnsureParent(path);await File.WriteAllBytesAsync(path,content,token);}
    public async Task<byte[]?> ReadBytesAsync(string path,CancellationToken token=default){EnsureManaged(path);return File.Exists(path)?await File.ReadAllBytesAsync(path,token):null;}
    public async Task<string?> ReadTextAsync(string path,CancellationToken token=default){EnsureManaged(path);return File.Exists(path)?await File.ReadAllTextAsync(path,token):null;}
    public Task<Stream?> OpenReadAsync(string path,CancellationToken token=default){EnsureManaged(path);Stream? stream=File.Exists(path)?new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read):null;return Task.FromResult(stream);}
    public Task DeleteIfExistsAsync(string path,CancellationToken token=default){EnsureManaged(path);if(File.Exists(path))File.Delete(path);return Task.CompletedTask;}
    private void EnsureParent(string path){EnsureManaged(path);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);}
    private void EnsureManaged(string path){var full=Path.GetFullPath(path);if(!full.StartsWith(_rootWithSeparator,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("The template path is outside the configured storage root.");}
}
