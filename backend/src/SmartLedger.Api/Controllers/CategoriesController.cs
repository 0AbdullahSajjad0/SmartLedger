using Microsoft.AspNetCore.Mvc;
using SmartLedger.Api.Common;
using SmartLedger.Application.Categories;

namespace SmartLedger.Api.Controllers;

[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<CategoryDto>> List(CancellationToken cancellationToken) =>
        categoryService.ListAsync(cancellationToken);

    [HttpGet("spending")]
    [ProducesResponseType<CategoryMonthDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Spending([FromQuery] string? month, CancellationToken cancellationToken) =>
        (await categoryService.GetMonthAsync(month, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateCategoryRequest request, CancellationToken cancellationToken) =>
        (await categoryService.CreateAsync(request, cancellationToken)).ToActionResult(this);

    [HttpPut("{id:guid}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken) =>
        (await categoryService.UpdateAsync(id, request, cancellationToken)).ToActionResult(this);
}
