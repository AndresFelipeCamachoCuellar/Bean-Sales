using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Constants;
using Web.Data;
using Web.Models;

namespace Web.Controllers;

[Authorize(Roles = Roles.SuperAdmin)] // Only SuperAdmin
public class ProvidersController : Controller
{
    private readonly ApplicationDbContext _context;

    public ProvidersController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var providers = await _context.Providers
            .OrderByDescending(p => p.CreatedOn)
            .ToListAsync();
        return View(providers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id)
    {
        var provider = await _context.Providers.FindAsync(id);
        if (provider == null) return NotFound();

        provider.ApprovalStatus = ApprovalStatus.Approved;
        provider.ApprovalDate = DateTime.Now;
        // provider.ModifiedBy = User.Identity.Name;

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id)
    {
        var provider = await _context.Providers.FindAsync(id);
        if (provider == null) return NotFound();

        provider.ApprovalStatus = ApprovalStatus.Rejected;
        // provider.ModifiedBy = User.Identity.Name;

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
