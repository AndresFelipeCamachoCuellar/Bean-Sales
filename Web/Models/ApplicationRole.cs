using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Web.Services.Tenancy;

namespace Web.Models;

// IOptionallyProviderOwned: ProviderID == null significa "rol global de Bean"
// (SuperAdmin, Admin, Basic, ProviderAdmin). Los roles creados desde CompanyRoles
// llevan el ProviderID de la empresa que los creó.
public class ApplicationRole : IdentityRole<Guid>, IOptionallyProviderOwned
{
    public ApplicationRole() : base() { }
    
    public ApplicationRole(string roleName) : base(roleName)
    {
        // Los roles creados por este constructor son los GLOBALES de Bean, donde el nombre
        // visible y el de Identity son el mismo. Los de proveedor se arman en
        // CompanyRolesController con RoleNaming.ForProvider.
        DisplayName = roleName;
        CreatedOn = DateTime.Now;
        Status = true;
    }

    /// <summary>
    /// Nombre que ve y escribe el usuario ("Bodeguero"). Es el ÚNICO que debe aparecer en la
    /// interfaz.
    ///
    /// Se separa del <c>Name</c> de Identity porque Identity impone un índice
    /// único GLOBAL sobre <c>NormalizedName</c>: sin esta separación, dos proveedores no
    /// podrían llamar "Bodeguero" a sus respectivos roles. Para los roles de proveedor,
    /// <c>Name</c> pasa a ser un identificador técnico prefijado
    /// (<c>p:{ProviderID:N}:{clave}</c>, ver <see cref="RoleNaming"/>); para los roles
    /// globales de Bean ambos valores coinciden.
    ///
    /// La unicidad del nombre visible se valida POR PROVEEDOR en el controlador, no en la
    /// base: dos empresas distintas pueden repetirlo sin enterarse la una de la otra.
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Display(Name = "Nombre del rol")]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    // Multi-tenancy
    public Guid? ProviderID { get; set; }
    
    [ForeignKey("ProviderID")]
    public Provider? Provider { get; set; }

    public bool Status { get; set; }

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }
}
