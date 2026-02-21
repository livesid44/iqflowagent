using IQFlowAgent.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Controllers;

[Authorize]
public class AnalysisController : Controller
{
    private readonly ApplicationDbContext _db;

    public AnalysisController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /Analysis — dashboard of all analyzed intakes
    public async Task<IActionResult> Index()
    {
        var records = await _db.IntakeRecords
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
        return View(records);
    }
}
