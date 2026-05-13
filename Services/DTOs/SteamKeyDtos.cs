namespace Services.DTOs;

public enum SteamKeyStatus
{
    Available = 0,
    Disabled = 1,
    Sold = 2
}

public class SteamKeyBulkUploadRequest
{
    public List<string> KeyValues { get; set; } = new();
}

public class SteamKeyBulkUploadResponse
{
    public int InsertedCount { get; set; }
    public int SkippedDuplicateCount { get; set; }
    public int InvalidRowCount { get; set; }
}

public class SteamKeyListResponse
{
    public List<SteamKeyListItemResponse> Data { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

public class SteamKeyListItemResponse
{
    public Guid Id { get; set; }
    /// <summary>
    /// Raw Steam key value — admins only. Masking is handled on the frontend for display purposes.
    /// </summary>
    public string Key { get; set; } = string.Empty;
    public int Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public Guid? OrderId { get; set; }
}

public class SteamKeyDetailResponse
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string KeyValue { get; set; } = string.Empty;
    public int Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public Guid? OrderId { get; set; }
}

public class SteamKeyUpdateRequest
{
    public string KeyValue { get; set; } = string.Empty;
}

public class SteamKeySummaryResponse
{
    public int Available { get; set; }
    public int Disabled { get; set; }
    public int Sold { get; set; }
    public int Total { get; set; }
}
