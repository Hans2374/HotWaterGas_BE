using Microsoft.AspNetCore.Mvc;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api/tags")]
public class TagsController : ControllerBase
{
    private readonly IProductCatalogService _productCatalogService;

    public TagsController(IProductCatalogService productCatalogService)
    {
        _productCatalogService = productCatalogService;
    }

    [HttpGet]
    public async Task<IActionResult> GetTags(CancellationToken cancellationToken)
    {
        var tags = await _productCatalogService.GetTagsAsync(cancellationToken);
        return Ok(tags);
    }
}