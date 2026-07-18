using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace CasePlanner.Web.Server.Tests;

// Minimal IWebHostEnvironment fake so PathService can be constructed in tests without a real
// ASP.NET Core host. PathService only reads ContentRootPath.
internal sealed class TestHostEnvironment : IWebHostEnvironment
{
    public TestHostEnvironment(string contentRootPath)
    {
        ContentRootPath = contentRootPath;
        WebRootPath = contentRootPath;
    }

    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "CasePlanner.Web.Server.Tests";
    public string WebRootPath { get; set; }
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
