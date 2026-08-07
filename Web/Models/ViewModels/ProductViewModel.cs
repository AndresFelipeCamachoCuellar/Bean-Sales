using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Web.Models.Enums;

namespace Web.Models.ViewModels;

public class ProductViewModel
{
    public string? ProductId { get; set; }

    [Required(ErrorMessage = "El nombre es obligatorio")]
    [Display(Name = "Nombre del Producto")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Descripción")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// COSTO del proveedor. Es lo que edita el proveedor en su formulario.
    /// </summary>
    [Required(ErrorMessage = "Tu precio es obligatorio")]
    [Display(Name = "Tu precio (COP)")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Tu precio debe ser mayor a 0")]
    public decimal SupplierPrice { get; set; }

    /// <summary>
    /// PVP (precio de venta al público). Solo lo edita quien tenga <c>Pricing/Update</c>.
    /// ⚠️ SIN <c>[Range]</c> a propósito: cuando el campo no se renderiza llega 0 y una
    /// validación de rango bloquearía el formulario del proveedor. La validación real es
    /// condicional y vive en <c>ProductsController</c>.
    /// </summary>
    [Display(Name = "Precio de venta al público (COP)")]
    public decimal Price { get; set; }

    /// <summary>
    /// ⚠️ NO confiar en el valor posteado: el controlador lo RECALCULA siempre desde el
    /// permiso real del usuario antes de decidir nada. Existe solo para que la vista sepa
    /// si debe renderizar el bloque de PVP.
    /// </summary>
    public bool CanEditSalePrice { get; set; }

    /// <summary>Motivo obligatorio cuando el PVP queda bajo el margen mínimo.</summary>
    [Display(Name = "Motivo")]
    [StringLength(300)]
    public string? SalePriceReason { get; set; }

    // --- Solo lectura: los llena el controlador para pintar el panel de margen ---
    public decimal TargetMarginPercent { get; set; }
    public decimal MinimumMarginPercent { get; set; }
    public decimal RoundingStep { get; set; }
    public bool MarginAlert { get; set; }
    public DateTime? PriceSetAt { get; set; }
    public string? PriceSetBy { get; set; }

    [Required(ErrorMessage = "El stock inicial es obligatorio")]
    [Display(Name = "Unidades Disponibles")]
    [Range(1, int.MaxValue, ErrorMessage = "El stock debe ser al menos 1")]
    public int Stock { get; set; }

    [Display(Name = "Imagen del Producto")]
    public string? ImageUrl { get; set; }

    public ProductStatus Status { get; set; }

    public string? RejectionReason { get; set; }

    // --- Atributos de café de especialidad ---
    [Display(Name = "Origen (región)")]
    public string? Origin { get; set; }

    [Display(Name = "Finca")]
    public string? Farm { get; set; }

    [Display(Name = "Altura")]
    public string? Altitude { get; set; }

    [Display(Name = "Proceso")]
    public CoffeeProcess? Process { get; set; }

    [Display(Name = "Variedad")]
    public string? Variety { get; set; }

    [Display(Name = "Lote")]
    public string? Lot { get; set; }

    [Display(Name = "Fecha de tueste")]
    [DataType(DataType.Date)]
    public DateTime? RoastDate { get; set; }

    [Display(Name = "Notas de cata")]
    public string? TastingNotes { get; set; }

    [Display(Name = "Rating")]
    [Range(0, 5, ErrorMessage = "El rating debe estar entre 0 y 5")]
    public decimal? Rating { get; set; }

    [Display(Name = "Nº de reseñas")]
    public int? ReviewCount { get; set; }

    // --- Empaque y envío (opcional: si falta, se usan los valores por defecto de configuración) ---
    // Cotas ENTERAS a propósito: así el atributo data-val-range que emite jQuery Validation
    // no lleva coma decimal (la cultura es-CO la escribiría como "0,1" y rompería la validación).
    [Display(Name = "Peso por unidad (g)")]
    [Range(1, 50000, ErrorMessage = "El peso debe estar entre 1 y 50000 gramos")]
    public int? ShippingWeightGrams { get; set; }

    [Display(Name = "Largo (cm)")]
    [Range(1, 200, ErrorMessage = "El largo debe estar entre 1 y 200 cm")]
    public decimal? LengthCm { get; set; }

    [Display(Name = "Ancho (cm)")]
    [Range(1, 200, ErrorMessage = "El ancho debe estar entre 1 y 200 cm")]
    public decimal? WidthCm { get; set; }

    [Display(Name = "Alto (cm)")]
    [Range(1, 200, ErrorMessage = "El alto debe estar entre 1 y 200 cm")]
    public decimal? HeightCm { get; set; }

    // Logic for Country Selection
    [Display(Name = "Países Objetivo")]
    public List<Guid> SelectedCountryIds { get; set; } = new List<Guid>();

    public IEnumerable<SelectListItem>? AvailableCountries { get; set; }

    /// <summary>
    /// Galería de fotos del lote. Solo se llena en <c>Edit</c> (GET): en <c>Create</c> el
    /// producto todavía no existe, así que no hay a qué colgar las fotos. Queda NULL en
    /// el POST (no se renderiza dentro del form), y la vista lo trata como opcional.
    /// </summary>
    public ProductImageManagerViewModel? ImageManager { get; set; }
}
