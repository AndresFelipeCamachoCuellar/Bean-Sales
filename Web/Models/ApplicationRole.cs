using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Web.Models;

public class ApplicationRole : IdentityRole<Guid>
{
    [Required]
    public string Description { get; set; } = string.Empty;

    public bool Status { get; set; }

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }
}
