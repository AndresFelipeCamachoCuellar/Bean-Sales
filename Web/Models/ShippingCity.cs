using System.ComponentModel.DataAnnotations;

namespace Web.Models;

/// <summary>
/// Catálogo de municipios de Colombia (DIVIPOLA) usado como destino de envío.
/// Vive en BD —y no solo en caché— para que el checkout pueda renderizar aunque
/// la API de la transportadora esté caída (ver ADR-014 §2.2, opción A).
/// </summary>
public class ShippingCity
{
    [Key]
    public Guid ShippingCityID { get; set; }

    /// <summary>Código DANE del municipio (DIVIPOLA, 5 dígitos: 2 de departamento + 3 de municipio).</summary>
    [Required]
    [StringLength(10)]
    public string DaneCode { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string Department { get; set; } = string.Empty;

    /// <summary>Identificador interno de la transportadora si difiere del DANE. Se llena en la fase de integración real.</summary>
    [StringLength(60)]
    public string? MipaqueteLocationCode { get; set; }

    /// <summary>Soft delete / cobertura: se puede apagar un municipio sin borrarlo.</summary>
    public bool Status { get; set; } = true;

    public DateTime CreatedOn { get; set; }
}
