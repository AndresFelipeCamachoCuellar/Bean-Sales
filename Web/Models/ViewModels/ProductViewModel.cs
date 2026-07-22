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

    [Required(ErrorMessage = "El precio es obligatorio")]
    [Display(Name = "Precio (USD)")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser mayor a 0")]
    public decimal Price { get; set; }

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

    // Logic for Country Selection
    [Display(Name = "Países Objetivo")]
    public List<Guid> SelectedCountryIds { get; set; } = new List<Guid>();

    public IEnumerable<SelectListItem>? AvailableCountries { get; set; }
}
