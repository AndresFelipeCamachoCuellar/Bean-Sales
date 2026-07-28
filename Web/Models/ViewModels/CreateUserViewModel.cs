using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Web.Models.ViewModels;

public class CreateUserViewModel
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Document Number")]
    public string DocumentNumber { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Document Type")]
    public Guid DocumentTypeID { get; set; }

    [Required]
    [Display(Name = "Country")]
    public Guid CountryID { get; set; }

    [Required]
    [Display(Name = "Role")]
    public Guid RoleID { get; set; }

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Date of Birth")]
    public DateTime DateOfBirth { get; set; }

    [Display(Name = "Gender")]
    public string Gender { get; set; } = string.Empty;

    // Dropdowns
    public IEnumerable<SelectListItem>? DocumentTypes { get; set; }
    public IEnumerable<SelectListItem>? Countries { get; set; }
    public IEnumerable<SelectListItem>? Roles { get; set; }
}
