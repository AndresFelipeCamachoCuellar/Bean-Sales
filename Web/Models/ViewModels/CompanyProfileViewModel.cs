using System.ComponentModel.DataAnnotations;

namespace Web.Models.ViewModels;

public class CompanyProfileViewModel
{
    public Guid ProviderID { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "Nombre de la Empresa")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string NIT { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Dirección")]
    public string Address { get; set; } = string.Empty;

    [StringLength(20)]
    [Display(Name = "Teléfono")]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(100)]
    [Display(Name = "Email Corporativo")]
    public string Email { get; set; } = string.Empty;

    // Read-Only Status Info
    [Display(Name = "Estado de Aprobación")]
    public string ApprovalStatus { get; set; } = string.Empty;
}
