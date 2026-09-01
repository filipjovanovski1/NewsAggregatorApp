using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace NewsApplication.Repository.Db.Configurations;

/// <summary>
/// Value comparers for collections mapped to jsonb. EF compares reference-typed properties by
/// reference unless told otherwise, so a List&lt;string&gt; mutated in place is not detected as
/// changed and the update is silently dropped.
///
/// This is the same shape as the categoriesComparer defined inline in
/// ApplicationDbContext.OnModelCreating for Article.Categories, lifted out so the discovery
/// configurations do not each carry their own copy.
/// </summary>
internal static class JsonbComparers
{
    public static readonly ValueComparer<List<string>> StringList = new(
        (a, b) =>
            a != null && b != null &&
            a.Count == b.Count &&
            a.SequenceEqual(b, StringComparer.Ordinal),
        a => a.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode())),
        a => a.ToList()
    );
}
