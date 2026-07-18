namespace CasePlanner.Web.Server.Security;

public interface IApplicationActorContext
{
    Guid? UserId { get; }
    string AuditLabel { get; }
}

public sealed class HttpApplicationActorContext(IHttpContextAccessor accessor):IApplicationActorContext
{
    private AuthenticatedUserProfile? Profile=>accessor.HttpContext?.Items[EntraUserProvisioningMiddleware.ProfileItemKey] as AuthenticatedUserProfile;
    public Guid? UserId=>Profile?.Id;
    public string AuditLabel=>Profile is { } profile?$"{profile.DisplayName} [{profile.Id:D}]":"Local development user";
}

public sealed class LocalApplicationActorContext:IApplicationActorContext
{
    public Guid? UserId=>null;
    public string AuditLabel=>"Local development user";
}
