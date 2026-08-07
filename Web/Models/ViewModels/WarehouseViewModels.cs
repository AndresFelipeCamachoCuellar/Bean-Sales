using System.ComponentModel.DataAnnotations;

namespace Web.Models.ViewModels;

/// <summary>Formulario de creación/edición de bodega (HU-1.5).</summary>
public class WarehouseViewModel
{
    public Guid WarehouseID { get; set; }

    [Required(ErrorMessage = "El código es obligatorio.")]
    [StringLength(20, ErrorMessage = "El código no puede pasar de 20 caracteres.")]
    [Display(Name = "Código")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(100)]
    [Display(Name = "Nombre")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Selecciona el país de la bodega.")]
    [Display(Name = "País")]
    public Guid CountryID { get; set; }

    [Required(ErrorMessage = "La ciudad es obligatoria.")]
    [StringLength(100)]
    [Display(Name = "Ciudad")]
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// DANE de 5 dígitos (DIVIPOLA). ⚠️ Cali es <c>76001</c>; <c>760001</c> es el código
    /// POSTAL. Es lo que se usa como ORIGEN al cotizar el envío con Mipaquete.
    /// </summary>
    [StringLength(10)]
    [Display(Name = "Código DANE (origen del envío)")]
    public string? DaneCode { get; set; }

    [StringLength(200)]
    [Display(Name = "Dirección")]
    public string Address { get; set; } = string.Empty;

    [Display(Name = "Bodega por defecto de su país")]
    public bool IsDefault { get; set; }

    [Display(Name = "Operativa")]
    public bool IsActive { get; set; } = true;

    // ---- Solo lectura (contexto para la vista) ----
    public List<InventoryFilterOption> CountryOptions { get; set; } = new();

    /// <summary>Unidades físicas hoy en la bodega. Bloquea la desactivación si es &gt; 0.</summary>
    public int UnitsOnHand { get; set; }

    public bool IsNew => WarehouseID == Guid.Empty;
}

/// <summary>Una fila del listado de bodegas.</summary>
public class WarehouseRowViewModel
{
    public Guid WarehouseID { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string? DaneCode { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }

    public int UnitsOnHand { get; set; }
    public int UnitsReserved { get; set; }
    public int UnitsAvailable => UnitsOnHand - UnitsReserved;

    /// <summary>Cuántos lotes distintos tienen saldo aquí.</summary>
    public int ProductCount { get; set; }

    /// <summary>Una bodega con stock &gt; 0 no se puede desactivar ni dar de baja.</summary>
    public bool CanDeactivate => UnitsOnHand <= 0;
}

public class WarehouseIndexViewModel
{
    public List<WarehouseRowViewModel> Rows { get; set; } = new();
    public bool CanCreate { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }

    public int TotalUnits => Rows.Sum(r => r.UnitsOnHand);
    public int ActiveCount => Rows.Count(r => r.IsActive);
}
