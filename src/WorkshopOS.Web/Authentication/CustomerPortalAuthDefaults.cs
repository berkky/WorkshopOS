namespace WorkshopOS.Web.Authentication;

public static class CustomerPortalAuthDefaults
{
    public const string AuthenticationScheme = "WorkshopOS.CustomerPortal";
}

public static class CustomerPortalClaimTypes
{
    public const string SharePublicId = "workshopos:estimate_share_public_id";

    public const string SessionKind = "workshopos:customer_portal_session";
}
