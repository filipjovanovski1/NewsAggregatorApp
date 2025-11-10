namespace NewsApplication.Web.Controllers
{
    public sealed record ScalarInt(int Value);
    public sealed record ScalarTimestamp(DateTimeOffset? Value);
    public sealed record DbInfo(string Db, string Usr, string Host, int Port, string Schema, string Path);
}
