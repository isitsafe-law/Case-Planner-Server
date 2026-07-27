namespace CasePlanner.Web.Server.Security;

public interface IApplicationActorContext
{
    Guid? UserId { get; }
    string AuditLabel { get; }
    // Manager/Administrator Dashboard Milestone 1: resolved once per action and stamped onto every
    // activity_log write (actor_role_at_action) so a later audit view can show "who, acting as what
    // role" without re-deriving it from a point-in-time app_users snapshot. See resolution order on
    // HttpApplicationActorContext.Role below.
    string Role { get; }
}

public sealed class HttpApplicationActorContext(IHttpContextAccessor accessor, EntraOptions entraOptions):IApplicationActorContext
{
    private AuthenticatedUserProfile? Profile=>accessor.HttpContext?.Items[EntraUserProvisioningMiddleware.ProfileItemKey] as AuthenticatedUserProfile;
    public Guid? UserId=>Profile?.Id;
    public string AuditLabel=>Profile is { } profile?$"{profile.DisplayName} [{profile.Id:D}]":"Local development user";

    // Priority order: Administrator (claims-based, CaseAccessEvaluator.IsAdministrator) beats
    // manager_tier, which beats plain is_manager, which beats "just an authenticated Attorney" - a
    // Chief/Deputy Chief Counsel is also typically IsManager, so the tier must be checked first.
    // Falls back to "Local development user" (matching LocalApplicationActorContext.AuditLabel) when
    // there's no HttpContext/profile at all, e.g. Entra disabled/local dev.
    public string Role
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is not null && CaseAccessEvaluator.IsAdministrator(context.User, entraOptions)) return "Administrator";
            var profile = Profile;
            return profile?.ManagerTier switch
            {
                "ChiefCounsel" => "Chief Counsel",
                "DeputyChiefCounsel" => "Deputy Chief Counsel",
                _ => profile switch
                {
                    { IsManager: true } => "Manager",
                    not null => "Attorney",
                    null => "Local development user",
                }
            };
        }
    }
}

public sealed class LocalApplicationActorContext:IApplicationActorContext
{
    public Guid? UserId=>null;
    public string AuditLabel=>"Local development user";
    public string Role=>"Local development user";
}
