namespace MasidBaha.Application.Common;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

// Shared by the admin listings
public class PagedQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    public int NormalisedPage => Math.Max(Page, 1);
    public int NormalisedPageSize => Math.Clamp(PageSize, 1, 100);
}
