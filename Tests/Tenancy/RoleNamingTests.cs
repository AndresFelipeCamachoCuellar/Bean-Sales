using Microsoft.EntityFrameworkCore;
using Web.Constants;
using Web.Models;
using Web.Services.Tenancy;
using Xunit;

namespace Tests.Tenancy;

/// <summary>
/// Nombres de rol multi-tenant (E5).
///
/// El bug: Identity impone un índice único GLOBAL sobre <c>NormalizedName</c>, así que dos
/// empresas proveedoras no podían tener ambas un rol "Bodeguero" — la segunda recibía
/// «Role name is already taken», mensaje que además le confirmaba que otra empresa ya usaba
/// ese nombre.
///
/// Estos tests corren contra SQLite REAL, con el índice único creado de verdad: si la
/// separación entre nombre visible y nombre de Identity fallara, el <c>SaveChanges</c>
/// lanzaría en vez de pasar en verde.
/// </summary>
public class RoleNamingTests
{
    // ============================================ El caso que rompía el multi-tenant

    [Fact]
    public async Task Dos_proveedores_pueden_llamar_igual_a_su_rol()
    {
        using var db = new TenantTestDatabase();

        // El banco de pruebas ya siembra "Bodeguero" en las DOS empresas. Que la base se
        // haya creado sin violar el índice único ya es media prueba; aquí se comprueba que
        // ambos existen y que el usuario ve el mismo nombre en las dos.
        var roles = await db.Db.Roles
            .Where(r => r.ProviderID != null)
            .ToListAsync();

        Assert.Equal(2, roles.Count);
        Assert.All(roles, r => Assert.Equal("Bodeguero", r.DisplayName));
        Assert.Equal(2, roles.Select(r => r.ProviderID).Distinct().Count());

        // …y el nombre técnico sí es distinto, que es lo que satisface a Identity.
        Assert.Equal(2, roles.Select(r => r.NormalizedName).Distinct().Count());
    }

    [Fact]
    public async Task Un_tercer_proveedor_tambien_puede_llamarlo_Bodeguero()
    {
        using var db = new TenantTestDatabase();

        var terceroId = Guid.NewGuid();

        db.Db.Roles.Add(new ApplicationRole
        {
            Id = Guid.NewGuid(),
            // Exactamente lo que hace CompanyRolesController.Create.
            Name = RoleNaming.ForProvider(terceroId, "Bodeguero"),
            NormalizedName = RoleNaming.ForProvider(terceroId, "Bodeguero").ToUpperInvariant(),
            DisplayName = "Bodeguero",
            Description = "Rol del tercer proveedor",
            ProviderID = terceroId,
            Status = true,
            CreatedBy = "TEST",
            CreatedOn = DateTime.Now
        });

        // Sin el prefijo esto lanzaría DbUpdateException por el índice RoleNameIndex.
        var guardados = await db.Db.SaveChangesAsync();

        Assert.Equal(1, guardados);
        Assert.Equal(3, await db.Db.Roles.CountAsync(r => r.DisplayName == "Bodeguero"));
    }

    [Fact]
    public async Task El_indice_unico_de_Identity_sigue_activo()
    {
        using var db = new TenantTestDatabase();

        // Control negativo: si este test dejara de fallar, significaría que SQLite no está
        // creando el índice único y que los dos tests de arriba pasan por la razón
        // equivocada.
        var repetido = RoleNaming.ForProvider(db.ProviderAId, "Bodeguero");

        db.Db.Roles.Add(new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = repetido,
            NormalizedName = repetido.ToUpperInvariant(),
            DisplayName = "Bodeguero",
            Description = "Duplicado dentro del MISMO proveedor",
            ProviderID = db.ProviderAId,
            Status = true,
            CreatedBy = "TEST",
            CreatedOn = DateTime.Now
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.Db.SaveChangesAsync());
    }

    // ================================================== 🔴 Candado: roles de Bean

    [Fact]
    public async Task Los_roles_globales_de_Bean_conservan_su_nombre_literal()
    {
        using var db = new TenantTestDatabase();

        // Si este test falla, el SuperAdmin queda fuera de su propio panel: su nombre se usa
        // por valor en [Authorize(Roles = ...)], User.IsInRole(...) y AddToRoleAsync(...).
        var superAdmin = await db.Db.Roles.SingleAsync(r => r.Id == db.GlobalRoleId);

        Assert.Equal(Roles.SuperAdmin, superAdmin.Name);
        Assert.Equal(Roles.SuperAdmin.ToUpperInvariant(), superAdmin.NormalizedName);
        Assert.Null(superAdmin.ProviderID);
        Assert.False(RoleNaming.IsProviderScoped(superAdmin.Name));
    }

    [Theory]
    [InlineData(Roles.SuperAdmin)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Basic)]
    [InlineData(Roles.ProviderAdmin)]
    public void Ningun_nombre_global_se_confunde_con_uno_de_proveedor(string globalRoleName)
    {
        Assert.False(RoleNaming.IsProviderScoped(globalRoleName));
    }

    [Fact]
    public void El_nombre_de_proveedor_si_se_reconoce_como_tal()
    {
        var name = RoleNaming.ForProvider(Guid.NewGuid(), "Bodeguero");

        Assert.True(RoleNaming.IsProviderScoped(name));
        Assert.StartsWith(RoleNaming.ProviderPrefix, name);
    }

    // ================================================================ La clave

    [Fact]
    public void El_mismo_nombre_en_dos_proveedores_produce_nombres_tecnicos_distintos()
    {
        var a = RoleNaming.ForProvider(Guid.NewGuid(), "Bodeguero");
        var b = RoleNaming.ForProvider(Guid.NewGuid(), "Bodeguero");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void El_mismo_nombre_en_el_mismo_proveedor_es_determinista()
    {
        var proveedor = Guid.NewGuid();

        // De esto depende que la validación de duplicados detecte el choque ANTES de que lo
        // haga Identity con su mensaje delator.
        Assert.Equal(
            RoleNaming.ForProvider(proveedor, "Bodeguero"),
            RoleNaming.ForProvider(proveedor, "bodeguero"));
    }

    [Theory]
    [InlineData("Bodeguero", "bodeguero")]
    [InlineData("BODEGUERO", "bodeguero")]
    [InlineData("  Bodeguero  ", "bodeguero")]
    [InlineData("Jefe de bodega", "jefe-de-bodega")]
    [InlineData("Bodegüero", "bodeguero")]
    [InlineData("Coordinación", "coordinacion")]
    [InlineData("Jefe / Bodega", "jefe-bodega")]
    public void La_clave_ignora_mayusculas_tildes_y_simbolos(string entrada, string esperado)
    {
        Assert.Equal(esperado, RoleNaming.Key(entrada));
    }

    [Fact]
    public void Dos_nombres_que_solo_difieren_en_tildes_son_el_MISMO_rol()
    {
        // Es la regla que aplica DisplayNameTakenAsync: "Coordinación" y "Coordinacion" en la
        // misma empresa son un duplicado, no dos roles.
        Assert.Equal(RoleNaming.Key("Coordinación"), RoleNaming.Key("coordinacion"));
    }

    // ============================================================= Nombre visible

    [Fact]
    public void Display_prefiere_siempre_el_nombre_visible()
    {
        var role = new ApplicationRole
        {
            Name = RoleNaming.ForProvider(Guid.NewGuid(), "Bodeguero"),
            DisplayName = "Bodeguero"
        };

        Assert.Equal("Bodeguero", RoleNaming.Display(role));
    }

    [Fact]
    public void Display_degrada_bien_si_el_backfill_no_llego_a_esa_fila()
    {
        // Una fila anterior al backfill (DisplayName vacío) no debe pintar la tabla en
        // blanco ni mostrar el identificador técnico completo.
        var identityName = RoleNaming.ForProvider(Guid.NewGuid(), "Jefe de bodega");

        Assert.Equal("jefe-de-bodega", RoleNaming.Display(string.Empty, identityName));
    }

    [Fact]
    public void Display_de_un_rol_global_sin_backfill_devuelve_su_nombre()
    {
        Assert.Equal(Roles.SuperAdmin, RoleNaming.Display(null, Roles.SuperAdmin));
    }

    [Fact]
    public void El_nombre_tecnico_cabe_en_la_columna_de_Identity()
    {
        // AspNetRoles.Name es nvarchar(256). "p:" + 32 + ":" = 35, y la clave se recorta a
        // 100, así que ni un nombre absurdamente largo desborda.
        var name = RoleNaming.ForProvider(Guid.NewGuid(), new string('a', 500));

        Assert.True(name.Length <= 256, $"El nombre generado mide {name.Length} caracteres.");
    }
}
