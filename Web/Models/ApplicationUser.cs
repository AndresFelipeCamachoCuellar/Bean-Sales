using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Web.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    // IdentityUser already provides Id (mapped to UserID logic), UserName, Email, PasswordHash, etc.

    [Required]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    public string LastName { get; set; } = string.Empty;

    public DateTime DateOfBirth { get; set; }

    [Required]
    public string Gender { get; set; } = string.Empty;

    [Required]
    public Guid DocumentTypeID { get; set; }

    [ForeignKey("DocumentTypeID")]
    public DocumentType? DocumentType { get; set; }

    [Required]
    public string DocumentNumber { get; set; } = string.Empty;

    [Required]
    public Guid CountryID { get; set; }

    [ForeignKey("CountryID")]
    public Country? Country { get; set; }

    public bool Status { get; set; }

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }
}
