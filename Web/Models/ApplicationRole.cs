using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Web.Models;

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() : base() { }
    
    public ApplicationRole(string roleName) : base(roleName) 
    {
        CreatedOn = DateTime.Now;
        Status = true;
    }
    [Required]
    public string Description { get; set; } = string.Empty;

    // Multi-tenancy
    public Guid? ProviderID { get; set; }
    
    [ForeignKey("ProviderID")]
    public Provider? Provider { get; set; }

    public bool Status { get; set; }

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }
}
