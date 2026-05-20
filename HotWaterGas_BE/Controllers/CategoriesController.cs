using Microsoft.AspNetCore.Mvc;
using Services.DTOs;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly IProductCatalogService _productCatalogService;

    public CategoriesController(IProductCatalogService productCatalogService)
    {
        _productCatalogService = productCatalogService;
    }

    [HttpGet]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        var categories = await _productCatalogService.GetCategoriesAsync(cancellationToken);
        return Ok(categories);
    }

    [HttpGet("homepage")]
    public async Task<IActionResult> GetHomepageCategories(CancellationToken cancellationToken)
    {
        var categories = await _productCatalogService.GetHomepageCategoriesAsync(cancellationToken);
        return Ok(categories);
    }
}