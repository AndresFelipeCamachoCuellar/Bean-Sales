using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Web.Models.Enums;

namespace Web.Models;

public class Product
{
    [Key]
    public Guid ProductID { get; set; }

    [Required]
    public Guid ProviderID { get; set; }

    [ForeignKey("ProviderID")]
    public Provider? Provider { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Required]
    public int Stock { get; set; }

    public string? ImageUrl { get; set; }

    // Workflow Status
    public ProductStatus ProductStatus { get; set; } = ProductStatus.Draft;

    public string? ShippingDetails { get; set; } // Tracking number, courier, etc.
    
    public string? RejectionReason { get; set; }

    // Audit
    public bool Status { get; set; } = true; // Soft delete

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? UpdatedBy { get; set; }

    public DateTime? UpdatedOn { get; set; }

    // Navigation
    public ICollection<ProductCountry> ProductCountries { get; set; } = new List<ProductCountry>();
}
