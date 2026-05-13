using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services.DTOs;
using Services.Implementations;
using Services.Interfaces;

namespace Services;

public static class DependencyInjection
{
    public static IServiceCollection AddServs(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IProductCatalogService, ProductCatalogService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IAdminProductService, AdminProductService>();
        services.AddScoped<IAdminCategoryService, AdminCategoryService>();
        services.AddScoped<IAdminTagService, AdminTagService>();
        services.AddScoped<IAdminRoleService, AdminRoleService>();
        services.AddScoped<IAdminDiscountService, AdminDiscountService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<ISteamKeyService, SteamKeyService>();
        services.AddScoped<IImageUploadService, CloudinaryImageUploadService>();

        return services;
    }

    public static IServiceCollection AddAuthOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthTokenOptions>(configuration.GetSection(AuthTokenOptions.SectionName));
        services.Configure<JwtTokenOptions>(configuration.GetSection(JwtTokenOptions.SectionName));
        return services;
    }
}
