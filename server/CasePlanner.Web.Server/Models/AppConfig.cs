namespace CasePlanner.Web.Server.Models;

public sealed class AppConfig
{
    public string AppName { get; set; } = "ARDOT Legal Division Case Planner";
    public string ShortAppName { get; set; } = "ARDOT Case Planner";
    // Single source of truth for the app version. Bump this before cutting a release build
    // and name the release folder to match (e.g. CasePlannerWeb_v0.2.0) so a running instance's
    // Settings > Diagnostics page always identifies exactly which build it is.
    public string Version { get; set; } = "1.0.16";
    public string LocalUrl { get; set; } = "http://127.0.0.1:5188";
    public string RootPath { get; set; } = "";
    public string ClientDistPath { get; set; } = "";
    public string DataFolder { get; set; } = "";
    public string BackupsFolder { get; set; } = "";
    public string ExportsFolder { get; set; } = "";
    public string TemplatesFolder { get; set; } = "";
    public string DocumentTemplatesFolder { get; set; } = "";
    public string CustomDocumentTemplatesFolder { get; set; } = "";
    public string ReferenceFolder { get; set; } = "";
    public string LogsFolder { get; set; } = "";
    public string ImportSamplesFolder { get; set; } = "";
    public string DatabasePath { get; set; } = "";
    public bool IsReleaseLocal { get; set; }
}
