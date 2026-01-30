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

    // Logic for Country Selection
    [Display(Name = "Países Objetivo")]
    public List<Guid> SelectedCountryIds { get; set; } = new List<Guid>();

    public IEnumerable<SelectListItem>? AvailableCountries { get; set; }
}
