namespace Services.DTOs;

public class AdminDiscountListItemResponse
{
    public Guid Id { get; set; }
    public decimal Percentage { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
    public int ProductCount { get; set; }
}

public class AdminDiscountDetailResponse
{
    public Guid Id { get; set; }
    public decimal Percentage { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
    public int ProductCount { get; set; }
}

public class AdminDiscountUpsertRequest
{
    public decimal Percentage { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}
