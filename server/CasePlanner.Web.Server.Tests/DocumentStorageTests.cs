using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

public sealed class DocumentStorageTests:IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"cpw_storage_"+Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FileSystemProvider_WritesCaseScopedContentUnderConfiguredRoot()
    {
        var contentRoot=Path.Combine(_root,"app","server","CasePlanner.Web.Server");Directory.CreateDirectory(contentRoot);
        var paths=new PathService(new TestHostEnvironment(contentRoot));var sharedRoot=Path.Combine(_root,"shared-documents");
        var storage=new FileSystemDocumentStorage(paths,new DocumentStorageOptions{RootPath=sharedRoot});
        var path=storage.CreatePath(42,"memo.txt");await storage.WriteTextAsync(path,"central content");
        Assert.Equal(Path.Combine(Path.GetFullPath(sharedRoot),"cases","42","memo.txt"),path);
        Assert.Equal("central content",await storage.ReadTextAsync(path));
    }

    [Fact]
    public async Task FileSystemProvider_RejectsTraversalAndPathsOutsideRoot()
    {
        var contentRoot=Path.Combine(_root,"app","server","CasePlanner.Web.Server");Directory.CreateDirectory(contentRoot);
        var paths=new PathService(new TestHostEnvironment(contentRoot));var storage=new FileSystemDocumentStorage(paths,new DocumentStorageOptions{RootPath=Path.Combine(_root,"shared")});
        Assert.Throws<ArgumentException>(()=>storage.CreatePath(1,"..\\outside.txt"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>storage.ReadTextAsync(Path.Combine(_root,"outside.txt")));
    }

    public void Dispose(){try{if(Directory.Exists(_root))Directory.Delete(_root,true);}catch{}}
}
