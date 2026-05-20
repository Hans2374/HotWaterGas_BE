namespace Services.DTOs;

// ─── Dashboard Summary ─────────────────────────────────────────────────────────

public class DashboardSummaryResponse
{
    public decimal TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public int OrdersToday { get; set; }
    public int CompletedOrders { get; set; }
    public int CancelledOrders { get; set; }
    public int LowStockProducts { get; set; }
    public int TotalCustomers { get; set; }
}

// ─── Revenue Analytics ─────────────────────────────────────────────────────────

public class RevenueDataPoint
{
    public string Label { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
}

// ─── Order Status Analytics ────────────────────────────────────────────────────

public class OrderStatusCount
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

// ─── Low Stock Products ────────────────────────────────────────────────────────

public class LowStockProductItem
{
    public Guid ProductId { get; set; }
    public string ProductSlug { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ThumbnailImageUrl { get; set; } = string.Empty;
    public int AvailableKeys { get; set; }
    public bool IsActive { get; set; }
}

// ─── Inventory Alerts ──────────────────────────────────────────────────────────

public class InventoryAlertsResponse
{
    public List<LowStockProductItem> OutOfStockProducts { get; set; } = new();
    public List<LowStockProductItem> LowStockProducts { get; set; } = new();
}

// ─── Recent Orders ──────────────────────────────────────────────────────────────

public class RecentOrderItem
{
    public Guid OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
