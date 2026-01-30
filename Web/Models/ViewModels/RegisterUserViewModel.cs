using System.ComponentModel.DataAnnotations;

namespace Web.Models.ViewModels;

public class RegisterUserViewModel
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
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
    public Guid DocumentTypeID { get; set; }

    // Dropdowns
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>? DocumentTypes { get; set; }
}
