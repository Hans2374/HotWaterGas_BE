#nullable disable

namespace Repos.Models;

public class ProductTags
{
    public Guid ProductId { get; set; }
    public Guid TagId { get; set; }

    public virtual Products Product { get; set; }
    public virtual Tags Tag { get; set; }
}
