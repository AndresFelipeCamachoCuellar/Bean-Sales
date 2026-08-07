using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Attributes;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Models.ViewModels;
using Web.Services;

namespace Web.Controllers;

/// <summary>
/// CRUD de bodegas (HU-1.5). El modelo es multi-bodega y multi-país desde el inicio, así
/// que abrir una ubicación nueva —incluso en otro país— no debería requerir tocar código.
///
/// Dos reglas de negocio se defienden aquí, en el servidor:
///  1. Una bodega con stock físico &gt; 0 NO se puede desactivar ni dar de baja (el café no
///     desaparece porque se apague una casilla).
///  2. Debe haber exactamente UNA bodega por defecto por país: marcar otra desmarca la anterior.
/// </summary>
[Authorize]
public class WarehousesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IPermissionService _permissions;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<WarehousesController> _logger;

    public WarehousesController(
        ApplicationDbContext context,
        IPermissionService permissions,
        UserManager<ApplicationUser> userManager,
        ILogger<WarehousesController> logger)
    {
        _context = context;
        _permissions = permissions;
        _userManager = userManager;
        _logger = logger;
    }

    // ================================================================== Index

    [HasPermission(Modules.Warehouses, Permissions.Read)]
    public async Task<IActionResult> Index()
    {
        var currentUser = await _userManager.GetUserAsync(User);

        var vm = new WarehouseIndexViewModel
        {
            CanCreate = currentUser != null
                        && await _permissions.HasPermissionAsync(currentUser, Modules.Warehouses, Permissions.Create),
            CanUpdate = currentUser != null
                        && await _permissions.HasPermissionAsync(currentUser, Modules.Warehouses, Permissions.Update),
            CanDelete = currentUser != null
                        && await _permissions.HasPermissionAsync(currentUser, Modules.Warehouses, Permissions.Delete)
        };

        vm.Rows = await _context.Warehouses
            .Where(w => w.Status)
            .OrderByDescending(w => w.IsActive)
            .ThenByDescending(w => w.IsDefault)
            .ThenBy(w => w.Code)
            .Select(w => new WarehouseRowViewModel
            {
                WarehouseID = w.WarehouseID,
                Code = w.Code,
                Name = w.Name,
                City = w.City,
                CountryName = w.Country != null ? w.Country.Name : "—",
                DaneCode = w.DaneCode,
                IsDefault = w.IsDefault,
                IsActive = w.IsActive,
                UnitsOnHand = w.StockItems.Sum(s => (int?)s.QuantityOnHand) ?? 0,
                UnitsReserved = w.StockItems.Sum(s => (int?)s.QuantityReserved) ?? 0,
                ProductCount = w.StockItems.Count(s => s.QuantityOnHand != 0)
            })
            .ToListAsync();

        return View(vm);
    }

    // ================================================================= Create

    [HasPermission(Modules.Warehouses, Permissions.Create)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var vm = new WarehouseViewModel { IsActive = true };
        await FillOptionsAsync(vm);

        // Si el país todavía no tiene bodegas, la primera será la de por defecto.
        vm.IsDefault = !await _context.Warehouses.AnyAsync(w => w.Status);

        return View(vm);
    }

    [HasPermission(Modules.Warehouses, Permissions.Create)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WarehouseViewModel model)
    {
        await ValidateAsync(model, existingId: null);

        if (!ModelState.IsValid)
        {
            await FillOptionsAsync(model);
            return View(model);
        }

        var usuario = User.Identity?.Name ?? "SYSTEM";

        var warehouse = new Warehouse
        {
            WarehouseID = Guid.NewGuid(),
            Code = model.Code.Trim().ToUpperInvariant(),
            Name = model.Name.Trim(),
            CountryID = model.CountryID,
            City = model.City.Trim(),
            DaneCode = NormalizeDane(model.DaneCode),
            Address = (model.Address ?? string.Empty).Trim(),
            IsDefault = model.IsDefault,
            IsActive = model.IsActive,
            Status = true,
            CreatedBy = usuario,
            CreatedOn = DateTime.Now
        };

        // Primera bodega del país: es forzosamente la de por defecto (la regla dice
        // "exactamente una por país", no "como máximo una").
        var yaHayEnElPais = await _context.Warehouses
            .AnyAsync(w => w.Status && w.CountryID == model.CountryID);

        if (!yaHayEnElPais) warehouse.IsDefault = true;

        _context.Warehouses.Add(warehouse);

        if (warehouse.IsDefault)
        {
            await ClearOtherDefaultsAsync(warehouse.CountryID, warehouse.WarehouseID, usuario);
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Bodega {Code} creada por {Usuario}.", warehouse.Code, usuario);
        TempData["InventoryOk"] = $"Bodega {warehouse.Code} creada.";

        return RedirectToAction(nameof(Index));
    }

    // =================================================================== Edit

    [HasPermission(Modules.Warehouses, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var warehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.WarehouseID == id && w.Status);
        if (warehouse == null) return NotFound();

        var vm = new WarehouseViewModel
        {
            WarehouseID = warehouse.WarehouseID,
            Code = warehouse.Code,
            Name = warehouse.Name,
            CountryID = warehouse.CountryID,
            City = warehouse.City,
            DaneCode = warehouse.DaneCode,
            Address = warehouse.Address,
            IsDefault = warehouse.IsDefault,
            IsActive = warehouse.IsActive,
            UnitsOnHand = await UnitsOnHandAsync(id)
        };

        await FillOptionsAsync(vm);
        return View(vm);
    }

    [HasPermission(Modules.Warehouses, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(WarehouseViewModel model)
    {
        var warehouse = await _context.Warehouses
            .FirstOrDefaultAsync(w => w.WarehouseID == model.WarehouseID && w.Status);

        if (warehouse == null) return NotFound();

        var unitsOnHand = await UnitsOnHandAsync(warehouse.WarehouseID);
        model.UnitsOnHand = unitsOnHand;

        await ValidateAsync(model, existingId: warehouse.WarehouseID);

        // 🔒 Regla 1: no se apaga una bodega que todavía tiene café dentro.
        if (warehouse.IsActive && !model.IsActive && unitsOnHand > 0)
        {
            ModelState.AddModelError(nameof(model.IsActive),
                $"Esta bodega todavía tiene {unitsOnHand} unidades físicas. Trasládalas o ajústalas a 0 antes de desactivarla.");
        }

        // 🔒 Regla 2: no puede quedar un país sin bodega por defecto.
        if (warehouse.IsDefault && !model.IsDefault)
        {
            ModelState.AddModelError(nameof(model.IsDefault),
                "No puedes quitar la marca de bodega por defecto: márcala en otra bodega del mismo país y esta se desmarcará sola.");
        }

        if (!ModelState.IsValid)
        {
            await FillOptionsAsync(model);
            return View(model);
        }

        var usuario = User.Identity?.Name ?? "SYSTEM";

        warehouse.Code = model.Code.Trim().ToUpperInvariant();
        warehouse.Name = model.Name.Trim();
        warehouse.CountryID = model.CountryID;
        warehouse.City = model.City.Trim();
        warehouse.DaneCode = NormalizeDane(model.DaneCode);
        warehouse.Address = (model.Address ?? string.Empty).Trim();
        warehouse.IsDefault = model.IsDefault;
        warehouse.IsActive = model.IsActive;
        warehouse.ModifiedBy = usuario;
        warehouse.ModifiedOn = DateTime.Now;

        if (warehouse.IsDefault)
        {
            await ClearOtherDefaultsAsync(warehouse.CountryID, warehouse.WarehouseID, usuario);
        }

        await _context.SaveChangesAsync();

        TempData["InventoryOk"] = $"Bodega {warehouse.Code} actualizada.";
        return RedirectToAction(nameof(Index));
    }

    // ================================================================= Delete

    /// <summary>
    /// Baja LÓGICA (<c>Status = false</c>). Nunca se borra la fila: el libro de movimientos
    /// referencia la bodega y borrarla dejaría el histórico sin poder explicarse.
    /// </summary>
    [HasPermission(Modules.Warehouses, Permissions.Delete)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var warehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.WarehouseID == id && w.Status);
        if (warehouse == null) return NotFound();

        var unitsOnHand = await UnitsOnHandAsync(id);
        if (unitsOnHand > 0)
        {
            TempData["InventoryError"] =
                $"La bodega {warehouse.Code} tiene {unitsOnHand} unidades físicas: no se puede dar de baja. " +
                "Traslada o ajusta el inventario a 0 primero.";
            return RedirectToAction(nameof(Index));
        }

        // Si era la de por defecto, hay que dejar otra en su lugar o el país se queda huérfano.
        if (warehouse.IsDefault)
        {
            var sustituta = await _context.Warehouses
                .Where(w => w.Status && w.IsActive && w.CountryID == warehouse.CountryID
                            && w.WarehouseID != warehouse.WarehouseID)
                .OrderBy(w => w.Code)
                .FirstOrDefaultAsync();

            if (sustituta == null)
            {
                TempData["InventoryError"] =
                    $"La bodega {warehouse.Code} es la única de su país. Crea otra antes de darla de baja: " +
                    "sin bodega por defecto el checkout no sabe desde dónde despachar.";
                return RedirectToAction(nameof(Index));
            }

            sustituta.IsDefault = true;
            sustituta.ModifiedBy = User.Identity?.Name ?? "SYSTEM";
            sustituta.ModifiedOn = DateTime.Now;
        }

        warehouse.Status = false;
        warehouse.IsActive = false;
        warehouse.IsDefault = false;
        warehouse.ModifiedBy = User.Identity?.Name ?? "SYSTEM";
        warehouse.ModifiedOn = DateTime.Now;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Bodega {Code} dada de baja por {Usuario}.", warehouse.Code, User.Identity?.Name ?? "SYSTEM");

        TempData["InventoryOk"] = $"Bodega {warehouse.Code} dada de baja.";
        return RedirectToAction(nameof(Index));
    }

    // ================================================================ Helpers

    private async Task ValidateAsync(WarehouseViewModel model, Guid? existingId)
    {
        if (!string.IsNullOrWhiteSpace(model.Code))
        {
            var code = model.Code.Trim().ToUpperInvariant();

            // El índice único de la BD lo impediría igual, pero con una excepción fea:
            // aquí se traduce a un mensaje que el operador entiende.
            var duplicado = await _context.Warehouses
                .AnyAsync(w => w.Code == code && (existingId == null || w.WarehouseID != existingId.Value));

            if (duplicado)
            {
                ModelState.AddModelError(nameof(model.Code), $"Ya existe una bodega con el código {code}.");
            }
        }

        if (model.CountryID == Guid.Empty)
        {
            ModelState.AddModelError(nameof(model.CountryID), "Selecciona el país de la bodega.");
        }
        else if (!await _context.Countries.AnyAsync(c => c.CountryID == model.CountryID))
        {
            ModelState.AddModelError(nameof(model.CountryID), "El país seleccionado no existe.");
        }

        var dane = NormalizeDane(model.DaneCode);
        if (!string.IsNullOrEmpty(dane) && (dane.Length != 5 || !dane.All(char.IsDigit)))
        {
            // Es el error que ya se cometió una vez: 760001 es el código POSTAL de Cali,
            // el DANE es 76001. Con 6 dígitos mipaquete devuelve "sin cobertura".
            ModelState.AddModelError(nameof(model.DaneCode),
                "El código DANE son 5 dígitos (DIVIPOLA). Cali es 76001 — 760001 es el código POSTAL, no el DANE.");
        }
    }

    private async Task FillOptionsAsync(WarehouseViewModel model)
    {
        model.CountryOptions = await _context.Countries
            .OrderBy(c => c.Name)
            .Select(c => new InventoryFilterOption { Id = c.CountryID, Label = c.Name })
            .ToListAsync();
    }

    /// <summary>Unidades FÍSICAS en la bodega. Es el número que bloquea la desactivación.</summary>
    private async Task<int> UnitsOnHandAsync(Guid warehouseId) =>
        await _context.StockItems
            .Where(s => s.WarehouseID == warehouseId)
            .SumAsync(s => (int?)s.QuantityOnHand) ?? 0;

    /// <summary>
    /// Deja una sola bodega por defecto en el país. No llama a SaveChanges: el llamador
    /// guarda todo junto.
    /// </summary>
    private async Task ClearOtherDefaultsAsync(Guid countryId, Guid keepId, string usuario)
    {
        var otras = await _context.Warehouses
            .Where(w => w.CountryID == countryId && w.IsDefault && w.WarehouseID != keepId)
            .ToListAsync();

        foreach (var w in otras)
        {
            w.IsDefault = false;
            w.ModifiedBy = usuario;
            w.ModifiedOn = DateTime.Now;
        }
    }

    private static string? NormalizeDane(string? dane)
    {
        if (string.IsNullOrWhiteSpace(dane)) return null;
        return dane.Trim();
    }
}
