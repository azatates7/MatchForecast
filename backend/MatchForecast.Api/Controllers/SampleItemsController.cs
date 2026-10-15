using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Models.Request;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatchForecast.Api.Controllers;

// Supabase bağlantısını uçtan uca denemek için örnek CRUD; fallback policy gereği Bearer token ister.
[ApiController]
[Route("api/sample-items")]
public sealed class SampleItemsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SampleItem>>> GetAll(CancellationToken ct)
    {
        return await db.SampleItems.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SampleItem>> Get(int id, CancellationToken ct)
    {
        var item = await db.SampleItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null)
            return NotFound();

        return item;
    }

    [HttpPost]
    public async Task<ActionResult<SampleItem>> Create(SampleItemRequest request, CancellationToken ct)
    {
        var item = new SampleItem { Name = request.Name, Description = request.Description };
        db.SampleItems.Add(item);
        await db.SaveChangesAsync(ct); // Id ve CreatedAt veritabanından geri okunur.
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SampleItemRequest request, CancellationToken ct)
    {
        var item = await db.SampleItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null)
            return NotFound();

        item.Name = request.Name;
        item.Description = request.Description;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await db.SampleItems.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? NotFound() : NoContent();
    }
}