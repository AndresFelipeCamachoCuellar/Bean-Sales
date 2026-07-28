using System.ComponentModel.DataAnnotations;

namespace Web.Models;

public class Country
{
    [Key]
    public Guid CountryID { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public bool Status { get; set; }

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }

    public ICollection<ProductCountry> ProductCountries { get; set; } = new List<ProductCountry>();
}
