#nullable disable

namespace Repos.Models;

public class ProductCategories
{
    public Guid ProductId { get; set; }
    public Guid CategoryId { get; set; }

    public virtual Products Product { get; set; }
    public virtual Categories Category { get; set; }
}
