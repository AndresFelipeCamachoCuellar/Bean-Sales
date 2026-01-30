using System.ComponentModel.DataAnnotations;

namespace Web.Models;

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected
}

public class Provider
{
    [Key]
    public Guid ProviderID { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "Company Name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string NIT { get; set; } = string.Empty;

    [StringLength(200)]
    public string Address { get; set; } = string.Empty;

    [StringLength(20)]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(100)]
    public string Email { get; set; } = string.Empty;

    // Workflow
    public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Pending;
    public DateTime? ApprovalDate { get; set; }

    // Audit
    public bool Status { get; set; } = true;

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }

    // Navigation
    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public ICollection<ApplicationRole> Roles { get; set; } = new List<ApplicationRole>();
}
