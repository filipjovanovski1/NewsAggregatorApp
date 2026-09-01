namespace NewsApplication.Web.Controllers
{
    public sealed record ScalarInt(int Value);
    public sealed record ScalarTimestamp(DateTimeOffset? Value);
    public sealed record DbInfo(string Db, string Usr, string Host, int Port, string Schema, string Path);
    public sealed record CreateDiscoveryTargetRequest(
        string CountryIso2,
        Guid? CityId,
        int Priority = 0,
        int CadenceDays = 90);
    public sealed record SetDiscoveryTargetEnabledRequest(bool IsEnabled);
}
