using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Web.Models;

public class ProductCountry
{
    public Guid ProductID { get; set; }

    [ForeignKey("ProductID")]
    public Product? Product { get; set; }

    public Guid CountryID { get; set; }

    [ForeignKey("CountryID")]
    public Country? Country { get; set; }

    // Logic:
    // IsTargeted: Provider wants to sell here.
    // IsAvailable: Admin has approved/received stock for this country.
    
    public bool IsTargeted { get; set; } = false;
    public bool IsAvailable { get; set; } = false;
}
