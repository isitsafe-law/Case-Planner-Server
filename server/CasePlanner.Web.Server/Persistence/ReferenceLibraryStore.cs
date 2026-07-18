using System.Text.Json;
using System.Text.RegularExpressions;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IReferenceLibraryStore
{
    string Provider { get; }
    Task<List<ReferenceDocument>> GetAsync(CancellationToken token=default);
    Task<ReferenceDocument> SaveAsync(ReferenceDocumentUpdate model,CancellationToken token=default);
    Task DeleteAsync(string key,CancellationToken token=default);
}

public sealed class FileReferenceLibraryStore(PathService paths):IReferenceLibraryStore
{
    private const string MetadataFile=".reference-library.json";
    private static readonly Regex KeyPattern=new("^[a-z0-9][a-z0-9_-]{0,79}$",RegexOptions.Compiled|RegexOptions.IgnoreCase);
    private sealed class Metadata { public string Title { get; set; }=""; public string Description { get; set; }=""; public string FileName { get; set; }=""; public bool Deleted { get; set; } }
    private static readonly IReadOnlyDictionary<string,(string Title,string Description,string FileName)> Defaults=new Dictionary<string,(string,string,string)>(StringComparer.OrdinalIgnoreCase)
    {
        ["opening_statement_reyes"]=("Opening Statement — Reyes (Prior Case)","Real opening statement from a prior ARDOT condemnation trial. Reference only — copy from, don't auto-generate.","OpeningStatement_Reyes.txt"),
        ["direct_exam_fanning"]=("Direct Examination — Maxwell Fanning","Prior-case direct examination outline for an appraisal witness.","DirectExamination_MaxwellFanning.txt"),
        ["direct_exam_bartlett"]=("Direct Examination Questions — Ches Bartlett","Prior-case direct examination question outline.","DirectExamination_ChesBartlett.txt"),
        ["jury_instructions"]=("Jury Instructions (AMI Reference Library)","Arkansas Model Instruction reference language for condemnation trials.","JuryInstructions.txt")
    };
    public string Provider=>"FileSystem";
    private string Folder=>paths.Config.ReferenceFolder;
    private string MetadataPath=>Path.Combine(Folder,MetadataFile);

    public async Task<List<ReferenceDocument>> GetAsync(CancellationToken token=default)
    {
        paths.EnsureFolders();var metadata=await ReadMetadataAsync(token);var keys=new HashSet<string>(Defaults.Keys,StringComparer.OrdinalIgnoreCase);
        foreach(var key in metadata.Keys)keys.Add(key);
        foreach(var file in Directory.GetFiles(Folder,"*.txt")){var key=Path.GetFileNameWithoutExtension(file);if(KeyPattern.IsMatch(key))keys.Add(key);}
        var result=new List<ReferenceDocument>();
        foreach(var key in keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (metadata.TryGetValue(key, out var stored) && stored.Deleted) continue;
            var info=metadata.TryGetValue(key,out var custom)?custom:Defaults.TryGetValue(key,out var builtIn)?new Metadata{Title=builtIn.Title,Description=builtIn.Description,FileName=builtIn.FileName}:new Metadata{Title=key,FileName=$"{key}.txt"};
            var fileName=SafeFileName(info.FileName,key);var path=Path.Combine(Folder,fileName);var text=File.Exists(path)?await File.ReadAllTextAsync(path,token):"(Reference file not found on disk.)";
            result.Add(new(){Key=key,Title=string.IsNullOrWhiteSpace(info.Title)?key:info.Title,Description=info.Description,Text=text});
        }
        return result;
    }

    public async Task<ReferenceDocument> SaveAsync(ReferenceDocumentUpdate model,CancellationToken token=default)
    {
        ValidateKey(model.Key);if(string.IsNullOrWhiteSpace(model.Title))throw new ArgumentException("Reference document title is required.");
        paths.EnsureFolders();var metadata=await ReadMetadataAsync(token);var fileName=metadata.TryGetValue(model.Key,out var existing)?SafeFileName(existing.FileName,model.Key):$"{model.Key}.txt";
        metadata[model.Key]=new Metadata{Title=model.Title.Trim(),Description=model.Description?.Trim()??"",FileName=fileName,Deleted=false};
        await File.WriteAllTextAsync(Path.Combine(Folder,fileName),model.Text??"",token);await WriteMetadataAsync(metadata,token);
        return new(){Key=model.Key,Title=model.Title.Trim(),Description=model.Description?.Trim()??"",Text=model.Text??""};
    }

    public async Task DeleteAsync(string key,CancellationToken token=default)
    {
        ValidateKey(key);paths.EnsureFolders();var metadata=await ReadMetadataAsync(token);var fileName=metadata.TryGetValue(key,out var existing)?SafeFileName(existing.FileName,key):Defaults.TryGetValue(key,out var builtIn)?builtIn.FileName:$"{key}.txt";
        var path=Path.Combine(Folder,fileName);if(File.Exists(path))File.Delete(path);
        metadata.TryGetValue(key, out var existingMetadata);
        Defaults.TryGetValue(key, out var defaultMetadata);
        metadata[key]=new Metadata
        {
            Title=existingMetadata?.Title ?? defaultMetadata.Title ?? key,
            Description=existingMetadata?.Description ?? defaultMetadata.Description ?? "",
            FileName=fileName,
            Deleted=true
        };
        await WriteMetadataAsync(metadata,token);
    }

    private async Task<Dictionary<string,Metadata>> ReadMetadataAsync(CancellationToken token)
    {
        if(!File.Exists(MetadataPath))return new(StringComparer.OrdinalIgnoreCase);
        await using var stream=File.OpenRead(MetadataPath);return await JsonSerializer.DeserializeAsync<Dictionary<string,Metadata>>(stream,cancellationToken:token)??new(StringComparer.OrdinalIgnoreCase);
    }
    private async Task WriteMetadataAsync(Dictionary<string,Metadata> metadata,CancellationToken token)
    {
        await using var stream=File.Create(MetadataPath);await JsonSerializer.SerializeAsync(stream,metadata,new JsonSerializerOptions{WriteIndented=true},token);
    }
    private static void ValidateKey(string key){if(!KeyPattern.IsMatch(key))throw new ArgumentException("Key must contain only letters, numbers, hyphens, and underscores.");}
    private static string SafeFileName(string? fileName,string key){var name=string.IsNullOrWhiteSpace(fileName)?$"{key}.txt":Path.GetFileName(fileName);return name.EndsWith(".txt",StringComparison.OrdinalIgnoreCase)?name:$"{name}.txt";}
}
