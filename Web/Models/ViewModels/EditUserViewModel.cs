using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Web.Models.ViewModels;

public class EditUserViewModel
{
    public string Id { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

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

    [Display(Name = "Gender")]
    public string Gender { get; set; } = string.Empty;

    public bool Status { get; set; }

    // Dropdowns
    public IEnumerable<SelectListItem>? DocumentTypes { get; set; }
    public IEnumerable<SelectListItem>? Countries { get; set; }
    public IEnumerable<SelectListItem>? Roles { get; set; }
}
