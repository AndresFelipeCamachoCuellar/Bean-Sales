using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Web.Models;

public class Permission
{
    [Key]
    public Guid PermissionID { get; set; }

    [Required]
    public Guid RoleID { get; set; }

    [ForeignKey("RoleID")]
    public ApplicationRole? Role { get; set; }

    [Required]
    public Guid ParametricPermissionID { get; set; }

    [ForeignKey("ParametricPermissionID")]
    public ParametricPermission? ParametricPermission { get; set; }

    public bool Status { get; set; }

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }
}
