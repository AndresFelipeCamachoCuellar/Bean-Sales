using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Models.ViewModels;

namespace Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public AccountController(
        SignInManager<ApplicationUser> signInManager, 
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _context = context;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        returnUrl ??= Url.Content("~/");

        if (ModelState.IsValid)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user != null)
            {
                if (!user.Status)
                {
                    ModelState.AddModelError(string.Empty, "Su cuenta está inactiva. Por favor contacte al administrador.");
                    return View(model);
                }

                // Check Provider Status
                if (user.ProviderID.HasValue)
                {
                    // Load Provider explicitly if not loaded
                    if (user.Provider == null)
                    {
                        await _context.Entry(user).Reference(u => u.Provider).LoadAsync();
                    }

                    if (user.Provider != null && user.Provider.ApprovalStatus != ApprovalStatus.Approved)
                    {
                        ModelState.AddModelError(string.Empty, $"Su empresa está en estado: {user.Provider.ApprovalStatus}. No puede ingresar aún.");
                        return View(model);
                    }
                }

                var result = await _signInManager.PasswordSignInAsync(user.UserName!, model.Password, model.RememberMe, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    await MergeSessionCartAsync(user);
                    return LocalRedirect(returnUrl);
                }
                if (result.IsLockedOut)
                {
                    return RedirectToAction("Lockout");
                }
            }
            
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }
        return View(model);
    }

    private async Task MergeSessionCartAsync(ApplicationUser user)
    {
        var sessionJson = HttpContext.Session.GetString("ShoppingCart");
        if (string.IsNullOrEmpty(sessionJson)) return;

        var sessionCart = System.Text.Json.JsonSerializer.Deserialize<List<ShoppingCartItem>>(sessionJson);
        if (sessionCart == null || !sessionCart.Any()) return;

        var dbCart = await _context.ShoppingCartItems
            .Where(c => c.UserID == user.Id)
            .ToListAsync();

        foreach (var sessionItem in sessionCart)
        {
            var dbItem = dbCart.FirstOrDefault(c => c.ProductID == sessionItem.ProductID);
            if (dbItem != null)
            {
                dbItem.Quantity += sessionItem.Quantity;
            }
            else
            {
                _context.ShoppingCartItems.Add(new ShoppingCartItem
                {
                    ShoppingCartItemID = Guid.NewGuid(),
                    UserID = user.Id,
                    ProductID = sessionItem.ProductID,
                    Quantity = sessionItem.Quantity,
                    CreatedOn = DateTime.Now
                });
            }
        }
        
        await _context.SaveChangesAsync();
        HttpContext.Session.Remove("ShoppingCart");
        await _context.SaveChangesAsync();
        HttpContext.Session.Remove("ShoppingCart");
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Register(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        var model = new RegisterUserViewModel();
        model.DocumentTypes = await _context.DocumentTypes
            .Where(d => d.Status)
            .Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name })
            .ToListAsync();
        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterUserViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        returnUrl ??= Url.Content("~/");

        if (ModelState.IsValid)
        {
            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                DocumentNumber = model.DocumentNumber,
                DocumentTypeID = model.DocumentTypeID,
                CountryID = (await _context.Countries.FirstOrDefaultAsync(c => c.Name == "Colombia"))?.CountryID ?? Guid.Empty, // Default
                Status = true,
                CreatedBy = "SELF_REGISTER",
                CreatedOn = DateTime.Now,
                Gender = "Not Specified"
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, Roles.Basic);
                
                await _signInManager.SignInAsync(user, isPersistent: false);
                
                // IMPORTANT: Merge Cart after Registration too!
                await MergeSessionCartAsync(user);

                return LocalRedirect(returnUrl);
            }
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        model.DocumentTypes = await _context.DocumentTypes
            .Where(d => d.Status)
            .Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name })
            .ToListAsync();
            
        return View(model);
    }
    
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterProvider()
    {
        var model = new RegisterProviderViewModel();
        model.DocumentTypes = await _context.DocumentTypes
            .Where(d => d.Status)
            .Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name })
            .ToListAsync();
            
        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterProvider(RegisterProviderViewModel model)
    {
        if (ModelState.IsValid)
        {
            // 1. Check if Provider NIT already exists
            if (await _context.Providers.AnyAsync(p => p.NIT == model.NIT))
            {
                ModelState.AddModelError("NIT", "A company with this NIT is already registered.");
            }
            
            // 2. Check if Email is used
            var existingUser = await _userManager.FindByEmailAsync(model.UserEmail);
            if (existingUser != null)
            {
                 ModelState.AddModelError("UserEmail", "This email is already in use.");
            }

            if (ModelState.IsValid)
            {
                // Create Provider
                var provider = new Provider
                {
                    ProviderID = Guid.NewGuid(),
                    Name = model.CompanyName,
                    NIT = model.NIT,
                    Email = model.CompanyEmail,
                    Phone = model.CompanyPhone,
                    Address = model.Address,
                    ApprovalStatus = ApprovalStatus.Pending, // Default Pending
                    Status = true,
                    CreatedBy = "SELF_REGISTER",
                    CreatedOn = DateTime.Now
                };

                await _context.Providers.AddAsync(provider);
                await _context.SaveChangesAsync();

                // Create User
                var user = new ApplicationUser
                {
                    UserName = model.UserEmail, // Helper: Use email as username
                    Email = model.UserEmail,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    DocumentNumber = model.DocumentNumber,
                    DocumentTypeID = model.DocumentTypeID,
                    CountryID = (await _context.Countries.FirstOrDefaultAsync(c => c.Name == "Colombia"))?.CountryID ?? Guid.Empty, // Default or need Country Selector
                    ProviderID = provider.ProviderID,
                    Status = true, // User is Active, but Login checks Provider Approval
                    CreatedBy = "SELF_REGISTER",
                    CreatedOn = DateTime.Now,
                    Gender = "Not Specified"
                };

                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    // Assign Role
                    await _userManager.AddToRoleAsync(user, Roles.ProviderAdmin);

                    // Sign in? No, pending approval.
                    return RedirectToAction("RegistrationSuccess"); 
                }
                
                // If user creation failed, remove provider
                _context.Providers.Remove(provider);
                await _context.SaveChangesAsync();

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }
        }
        
        // Reload Dropdowns
        model.DocumentTypes = await _context.DocumentTypes
            .Where(d => d.Status)
            .Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name })
            .ToListAsync();
            
        return View(model);
    }
    
    [HttpGet]
    [AllowAnonymous]
    public IActionResult RegistrationSuccess()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        HttpContext.Session.Remove("ShoppingCart"); // Clear session cart on logout just in case
        return RedirectToAction("Index", "Home");
    }
}
