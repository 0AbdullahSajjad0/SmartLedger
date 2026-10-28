using Microsoft.AspNetCore.Mvc;
using SmartLedger.Application.Categories;

namespace SmartLedger.Api.Controllers;

[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    /// <summary>The signed-in user's categories, archived ones included, by name.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<CategoryDto>> List(CancellationToken cancellationToken) =>
        categoryService.ListAsync(cancellationToken);
}
