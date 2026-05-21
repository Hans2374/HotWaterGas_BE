namespace Services.DTOs;

public class FulfillmentEmailRequest
{
    public const string SectionName = "Frontend";

    public string ToEmail { get; set; } = string.Empty;
    public string ToName { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalTotal { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public List<FulfillmentOrderItem> Items { get; set; } = new();

    public FulfillmentEmailRequest WithResolvedItems(List<FulfillmentOrderItem> items)
        => new()
        {
            ToEmail = ToEmail,
            ToName = ToName,
            OrderCode = OrderCode,
            OrderDate = OrderDate,
            Subtotal = Subtotal,
            DiscountAmount = DiscountAmount,
            FinalTotal = FinalTotal,
            PaymentStatus = PaymentStatus,
            LogoUrl = LogoUrl,
            Items = items
        };
}

public class FulfillmentOrderItem
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public List<string> SteamKeys { get; set; } = new();

    public FulfillmentOrderItem WithResolvedImageUrl(string resolvedUrl)
        => new()
        {
            ProductId = ProductId,
            ProductName = ProductName,
            ProductImageUrl = resolvedUrl,
            Quantity = Quantity,
            UnitPrice = UnitPrice,
            LineTotal = LineTotal,
            SteamKeys = SteamKeys
        };
}
