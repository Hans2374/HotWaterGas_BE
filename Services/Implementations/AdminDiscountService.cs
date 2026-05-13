using Microsoft.EntityFrameworkCore;
using Repos;
using Repos.Models;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class AdminDiscountService : IAdminDiscountService
{
    private readonly HotWaterGasDBContext _dbContext;

    public AdminDiscountService(HotWaterGasDBContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<AdminDiscountListItemResponse>> GetDiscountsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var discounts = await _dbContext.Discounts
            .Include(d => d.Products)
            .AsNoTracking()
            .OrderByDescending(d => d.StartDate)
            .ToListAsync(cancellationToken);

        return discounts.Select(d => new AdminDiscountListItemResponse
        {
            Id = d.Id,
            Percentage = d.Percentage,
            StartDate = d.StartDate,
            EndDate = d.EndDate,
            IsActive = d.StartDate <= now && d.EndDate >= now,
            ProductCount = d.Products.Count
        }).ToList();
    }

    public async Task<AdminDiscountDetailResponse?> GetDiscountByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var discount = await _dbContext.Discounts
            .Include(d => d.Products)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (discount is null) return null;

        return new AdminDiscountDetailResponse
        {
            Id = discount.Id,
            Percentage = discount.Percentage,
            StartDate = discount.StartDate,
            EndDate = discount.EndDate,
            IsActive = discount.StartDate <= now && discount.EndDate >= now,
            ProductCount = discount.Products.Count
        };
    }

    public async Task<AdminDiscountDetailResponse> CreateDiscountAsync(AdminDiscountUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var discount = new Discounts
        {
            Id = Guid.NewGuid(),
            Percentage = request.Percentage,
            StartDate = request.StartDate,
            EndDate = request.EndDate
        };

        _dbContext.Discounts.Add(discount);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await GetDiscountByIdAsync(discount.Id, cancellationToken))!;
    }

    public async Task<AdminDiscountDetailResponse> UpdateDiscountAsync(Guid id, AdminDiscountUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var discount = await _dbContext.Discounts
            .Include(d => d.Products)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (discount is null)
            throw new KeyNotFoundException("Discount not found.");

        discount.Percentage = request.Percentage;
        discount.StartDate = request.StartDate;
        discount.EndDate = request.EndDate;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await GetDiscountByIdAsync(discount.Id, cancellationToken))!;
    }

    public async Task DeleteDiscountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var discount = await _dbContext.Discounts
            .Include(d => d.Products)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (discount is null)
            throw new KeyNotFoundException("Discount not found.");

        foreach (var product in discount.Products.ToList())
        {
            product.DiscountId = null;
        }

        _dbContext.Discounts.Remove(discount);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
