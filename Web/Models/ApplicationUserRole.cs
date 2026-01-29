using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Web.Models;

public class ApplicationUserRole : IdentityUserRole<Guid>
{
    [Key]
    public Guid UserRoleID { get; set; }

    public bool Status { get; set; }

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }
}
