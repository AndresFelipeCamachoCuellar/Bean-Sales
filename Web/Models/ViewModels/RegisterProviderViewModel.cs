using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering; // Fix for SelectListItem


namespace Web.Models.ViewModels;

public class RegisterProviderViewModel
{
    // Company Info
    [Required]
    [Display(Name = "Company Name")]
    [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters.", MinimumLength = 2)]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "NIT / Tax ID")]
    public string NIT { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Company Email")]
    public string CompanyEmail { get; set; } = string.Empty;

    [Phone]
    [Display(Name = "Phone")]
    public string CompanyPhone { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    // Admin User Info headers
    [Required]
    [Display(Name = "Admin First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Admin Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Document Number")]
    public string DocumentNumber { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Doc. Type")]
    public Guid DocumentTypeID { get; set; }

    [Required]
    [EmailAddress]
    [Display(Name = "Admin Email")]
    public string UserEmail { get; set; } = string.Empty;

    [Required]
    [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters.", MinimumLength = 6)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    // Dropdowns (Populated in Controller)
    public List<SelectListItem>? DocumentTypes { get; set; }
}
