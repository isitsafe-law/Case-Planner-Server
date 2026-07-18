namespace CasePlanner.Web.Server.Security;

public sealed class EntraUserProvisioningMiddleware(RequestDelegate next)
{
    public const string ProfileItemKey = "CasePlanner.AuthenticatedUser";

    public async Task InvokeAsync(HttpContext context, SqlServerAppUserRepository users)
    {
        if (context.User.Identity?.IsAuthenticated == true && context.Request.Path.StartsWithSegments("/api"))
        {
            var identity = EntraClaims.FromPrincipal(context.User);
            context.Items[ProfileItemKey] = await users.ProvisionAsync(identity, context.RequestAborted);
        }
        await next(context);
    }
}
