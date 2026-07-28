using System.ComponentModel.DataAnnotations;

namespace Web.Models;

public class DocumentType
{
    [Key]
    public Guid DocumentTypeID { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public bool Status { get; set; }

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }
}
