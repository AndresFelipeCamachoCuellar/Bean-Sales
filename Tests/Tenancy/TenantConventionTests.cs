using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Web.Services.Tenancy;
using Xunit;

namespace Tests.Tenancy;

/// <summary>
/// Red de seguridad para lo que viene: E3 (acuerdos y liquidaciones) va a introducir
/// entidades nuevas con <c>ProviderID</c>.
///
/// Si alguien agrega una y NO la marca con <see cref="IProviderOwned"/> o
/// <see cref="IOptionallyProviderOwned"/>, esa entidad queda FUERA del mecanismo de
/// aislamiento: <c>ApplyTo</c> ni siquiera compila contra ella, así que el controlador
/// nuevo escribirá el filtro a mano… o se le olvidará. Este test falla primero y avisa.
/// </summary>
public class TenantConventionTests
{
    [Fact]
    public void Toda_entidad_mapeada_con_ProviderID_esta_marcada_como_multitenant()
    {
        using var db = new TenantTestDatabase();

        var sinMarcar = db.Db.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .Distinct()
            .Where(TieneColumnaProviderId)
            .Where(t => !typeof(IProviderOwned).IsAssignableFrom(t)
                        && !typeof(IOptionallyProviderOwned).IsAssignableFrom(t))
            // Provider ES el tenant: su PK se llama ProviderID pero no se filtra por sí misma.
            .Where(t => t != typeof(Web.Models.Provider))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        Assert.True(
            sinMarcar.Count == 0,
            "Estas entidades tienen ProviderID pero no implementan IProviderOwned ni " +
            "IOptionallyProviderOwned, así que IProviderScope no las puede aislar: " +
            string.Join(", ", sinMarcar));
    }

    [Fact]
    public void Las_entidades_marcadas_exponen_una_propiedad_ProviderID_mapeada()
    {
        using var db = new TenantTestDatabase();

        // El filtro se construye por reflexión sobre la propiedad "ProviderID". Si una
        // entidad marcada la renombrara, ApplyTo reventaría en RUNTIME (y solo en la
        // pantalla que la use). Mejor que reviente aquí.
        foreach (var entity in db.Db.Model.GetEntityTypes())
        {
            var clr = entity.ClrType;

            var marcada = typeof(IProviderOwned).IsAssignableFrom(clr)
                          || typeof(IOptionallyProviderOwned).IsAssignableFrom(clr);

            if (!marcada) continue;

            Assert.NotNull(clr.GetProperty("ProviderID", BindingFlags.Public | BindingFlags.Instance));
            Assert.NotNull(entity.FindProperty("ProviderID"));
        }
    }

    private static bool TieneColumnaProviderId(Type clrType) =>
        clrType.GetProperty("ProviderID", BindingFlags.Public | BindingFlags.Instance) is not null;
}
