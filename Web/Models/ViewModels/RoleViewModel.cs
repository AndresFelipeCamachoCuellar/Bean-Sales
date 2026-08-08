using System.ComponentModel.DataAnnotations;

namespace Web.Models.ViewModels;

public class RoleViewModel
{
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Nombre VISIBLE del rol (<c>ApplicationRole.DisplayName</c>). Nunca es el nombre
    /// técnico de Identity: para un rol de proveedor ese sería <c>p:{guid}:{clave}</c>.
    /// </summary>
    [Required(ErrorMessage = "El nombre del rol es obligatorio.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 100 caracteres.")]
    [Display(Name = "Nombre del rol")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Descripción")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Rol protegido del sistema (<c>SuperAdmin</c> / <c>ProviderAdmin</c>): no se renombra
    /// ni se borra desde la interfaz. La vista lo usa para decidir qué acciones pinta; la
    /// autorización real vive igualmente en el controlador.
    /// </summary>
    public bool IsSystem { get; set; }

    // Optional: Count of users in this role?
    public int UserCount { get; set; }
}
