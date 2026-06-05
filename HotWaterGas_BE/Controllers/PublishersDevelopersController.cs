using Microsoft.AspNetCore.Mvc;
using Services.Interfaces;

namespace HotWaterGas_BE.Controllers;

[ApiController]
[Route("api")]
public class PublishersDevelopersController : ControllerBase
{
    private readonly IProductCatalogService _productCatalogService;

    public PublishersDevelopersController(IProductCatalogService productCatalogService)
    {
        _productCatalogService = productCatalogService;
    }

    [HttpGet("publishers")]
    public async Task<IActionResult> GetPublishers(CancellationToken cancellationToken = default)
    {
        var publishers = await _productCatalogService.GetPublishersAsync(cancellationToken);
        return Ok(publishers);
    }

    [HttpGet("publishers/{id:guid}")]
    public async Task<IActionResult> GetPublisherById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var publisher = await _productCatalogService.GetPublisherByIdAsync(id, cancellationToken);
        if (publisher is null)
        {
            return NotFound(new { message = "Publisher not found." });
        }

        return Ok(publisher);
    }

    [HttpGet("developers")]
    public async Task<IActionResult> GetDevelopers(CancellationToken cancellationToken = default)
    {
        var developers = await _productCatalogService.GetDevelopersAsync(cancellationToken);
        return Ok(developers);
    }

    [HttpGet("developers/{id:guid}")]
    public async Task<IActionResult> GetDeveloperById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var developer = await _productCatalogService.GetDeveloperByIdAsync(id, cancellationToken);
        if (developer is null)
        {
            return NotFound(new { message = "Developer not found." });
        }

        return Ok(developer);
    }
}
