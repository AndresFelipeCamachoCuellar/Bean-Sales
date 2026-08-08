using System.Globalization;
using System.Text;
using Web.Constants;
using Web.Models;

namespace Web.Services.Tenancy;

/// <summary>
/// Traduce entre el <b>nombre visible</b> de un rol (<see cref="ApplicationRole.DisplayName"/>,
/// lo que escribe y lee el usuario) y el <b>nombre de Identity</b>
/// (<see cref="ApplicationRole.Name"/>, la clave que ASP.NET Core Identity obliga a ser
/// ÚNICA en toda la base a través del índice <c>RoleNameIndex</c> sobre
/// <c>NormalizedName</c>).
///
/// ─────────────────────────────────────────────────────────────────────────────────────
/// EL PROBLEMA QUE RESUELVE
/// ─────────────────────────────────────────────────────────────────────────────────────
/// Ese índice es global, así que dos proveedores NO podían tener ambos un rol "Bodeguero":
/// el segundo recibía «Role name is already taken», un mensaje que además le confirmaba
/// que otra empresa ya usaba ese nombre. Eso rompe la promesa de la beta ("cada proveedor
/// gestiona sus propios roles") y filtra información de otro tenant.
///
/// La solución es separar los dos nombres: el visible se valida por proveedor, y el de
/// Identity se prefija con el proveedor para que la unicidad global se cumpla sola.
///
/// ─────────────────────────────────────────────────────────────────────────────────────
/// 🔴 CANDADO: LOS ROLES GLOBALES DE BEAN NO SE PREFIJAN NUNCA
/// ─────────────────────────────────────────────────────────────────────────────────────
/// <see cref="Roles.SuperAdmin"/>, <see cref="Roles.Admin"/>, <see cref="Roles.Basic"/> y
/// <see cref="Roles.ProviderAdmin"/> tienen <c>ProviderID == null</c> y se usan POR NOMBRE
/// en <c>[Authorize(Roles = ...)]</c>, <c>User.IsInRole(...)</c>, <c>AddToRoleAsync(...)</c>
/// y en el sembrado. Prefijarlos dejaría al SuperAdmin fuera de su propio panel y el sitio
/// quedaría inaccesible. <b>Solo se prefijan los roles con <c>ProviderID != null</c>.</b>
/// </summary>
public static class RoleNaming
{
    /// <summary>Marca que identifica un nombre de Identity generado para un proveedor.</summary>
    public const string ProviderPrefix = "p:";

    /// <summary>
    /// Tope de la parte legible. <c>"p:"</c> + 32 (GUID en formato N) + <c>":"</c> = 35, así
    /// que 100 deja el nombre en 135 y cabe de sobra en el <c>nvarchar(256)</c> de Identity.
    /// </summary>
    private const int MaxKeyLength = 100;

    /// <summary>
    /// Nombre de Identity de un rol de proveedor: <c>p:{providerId:N}:{clave}</c>.
    /// Es DETERMINISTA — el mismo nombre visible produce siempre el mismo nombre de
    /// Identity —, que es justo lo que permite detectar el duplicado dentro del proveedor
    /// comparando <see cref="Key"/> sin depender de la intercalación de la base de datos.
    /// </summary>
    public static string ForProvider(Guid providerId, string? displayName) =>
        $"{ProviderPrefix}{providerId:N}:{Key(displayName)}";

    /// <summary>¿Este nombre de Identity fue generado para un proveedor?</summary>
    public static bool IsProviderScoped(string? identityName) =>
        identityName is not null && identityName.StartsWith(ProviderPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Qué mostrar en pantalla. Cae al nombre de Identity solo para las filas anteriores al
    /// backfill (o para los roles globales de Bean, donde ambos coinciden), así que una
    /// migración a medias nunca deja la tabla en blanco.
    /// </summary>
    public static string Display(string? displayName, string? identityName)
    {
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName;
        return IsProviderScoped(identityName)
            ? identityName![(identityName.LastIndexOf(':') + 1)..]
            : identityName ?? string.Empty;
    }

    /// <inheritdoc cref="Display(string?, string?)"/>
    public static string Display(ApplicationRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return Display(role.DisplayName, role.Name);
    }

    /// <summary>
    /// Clave de comparación de nombres visibles: minúsculas, sin tildes y con los símbolos
    /// convertidos en guiones. Sirve para dos cosas a la vez:
    ///
    ///  · decidir si "Bodeguero", "bodeguero" y "Bodegüero" son el MISMO rol dentro de un
    ///    proveedor, sin depender de la intercalación (SQL Server compara sin distinguir
    ///    mayúsculas, SQLite sí las distingue: comparar en memoria da el mismo resultado en
    ///    producción y en los tests);
    ///  · construir la parte legible del nombre de Identity.
    /// </summary>
    public static string Key(string? displayName)
    {
        var slug = Slug(displayName);

        // Un nombre escrito íntegramente en un alfabeto no latino deja el slug vacío. En ese
        // caso se conserva el texto original en minúsculas: sigue siendo determinista y
        // sigue distinguiendo dos nombres distintos.
        if (slug.Length == 0)
        {
            slug = (displayName ?? string.Empty).Trim().ToLowerInvariant();
        }

        return slug.Length <= MaxKeyLength ? slug : slug[..MaxKeyLength];
    }

    private static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        // FormD separa la tilde de la letra ("á" -> "a" + ´), así que descartando las marcas
        // diacríticas queda el ASCII de base sin tener que mantener una tabla de reemplazos.
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character < 128 && char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 0) builder.Append('-');
                builder.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString();
    }
}
