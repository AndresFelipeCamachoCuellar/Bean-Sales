using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Web.Models;

public class ParametricPermission
{
    [Key]
    public Guid ParametricPermissionID { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Required]
    public Guid ModuleID { get; set; }

    [ForeignKey("ModuleID")]
    public ParametricModule? Module { get; set; }

    public bool Status { get; set; }

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }
}
