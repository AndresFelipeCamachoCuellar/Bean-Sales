using System.ComponentModel.DataAnnotations;

namespace Web.Models;

public class ParametricModule
{
    [Key]
    public Guid ModuleID { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;

    public bool Status { get; set; }

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }

    public ICollection<ParametricPermission>? ParametricPermissions { get; set; }
}
