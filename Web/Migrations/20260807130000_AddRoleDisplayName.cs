using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web.Migrations
{
    /// <summary>
    /// E5 — Los nombres de rol dejan de ser globalmente únicos para el usuario.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────────
    /// QUÉ ARREGLA
    /// ─────────────────────────────────────────────────────────────────────────────────
    /// ASP.NET Core Identity impone un índice único GLOBAL (<c>RoleNameIndex</c>) sobre
    /// <c>NormalizedName</c>. Con él, dos empresas proveedoras NO podían tener ambas un rol
    /// "Bodeguero": la segunda recibía «Role name is already taken», mensaje que además le
    /// confirmaba que otra empresa ya usaba ese nombre.
    ///
    /// A partir de aquí:
    ///   · <c>DisplayName</c> = lo que ve y escribe el usuario. Se valida POR PROVEEDOR.
    ///   · <c>Name</c> / <c>NormalizedName</c> = identificador TÉCNICO. Para los roles de
    ///     proveedor pasa a ser <c>p:{ProviderID sin guiones}:{nombre en minúsculas}</c>,
    ///     único por construcción. Nunca se muestra.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────────
    /// 🔴 LOS ROLES GLOBALES DE BEAN NO SE TOCAN
    /// ─────────────────────────────────────────────────────────────────────────────────
    /// <c>SuperAdmin</c>, <c>Admin</c>, <c>Basic</c> y <c>ProviderAdmin</c> tienen
    /// <c>ProviderID IS NULL</c> y se usan POR NOMBRE en <c>[Authorize(Roles = ...)]</c>,
    /// <c>User.IsInRole(...)</c> y <c>AddToRoleAsync(...)</c>. El UPDATE del paso 3 filtra
    /// por <c>ProviderID IS NOT NULL</c> justamente por eso: prefijarlos dejaría al
    /// SuperAdmin fuera de su propio panel.
    ///
    /// Las asignaciones usuario↔rol viven en <c>AspNetUserRoles</c> por <c>RoleId</c>, y
    /// <c>PermissionService</c> resuelve los permisos por <c>RoleID</c>: renombrar el
    /// <c>Name</c> NO desasigna a nadie ni le quita permisos. Lo único que cambia es el
    /// claim de rol dentro de la cookie de sesión, que Identity refresca por
    /// <c>SecurityStamp</c>.
    /// </summary>
    public partial class AddRoleDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ------------------------------------------------------------- 1. Esquema
            //
            // NOT NULL con defaultValue "" para que el ALTER no falle sobre las filas
            // existentes; el paso 2 lo rellena inmediatamente después.
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetRoles",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            // ------------------------------------------- 2. Backfill del nombre visible
            //
            // TODOS los roles (globales y de proveedor) conservan como nombre visible el que
            // tenían. Va ANTES del paso 3, que es el que destruye el Name original.
            migrationBuilder.Sql(@"
UPDATE [AspNetRoles]
SET    [DisplayName] = [Name]
WHERE  [Name] IS NOT NULL
  AND  ([DisplayName] IS NULL OR [DisplayName] = N'');
");

            // ---------------------------------- 3. Prefijo por tenant (SOLO proveedores)
            //
            // SQL Server evalúa TODAS las expresiones de la derecha contra los valores
            // PREVIOS de la fila, así que [Name] y [NormalizedName] se calculan ambos a
            // partir del nombre viejo aunque se asignen en el mismo UPDATE.
            //
            // El nombre técnico se arma con el nombre viejo en minúsculas y los espacios
            // como guiones: es legible al depurar en la base y ya no puede chocar entre
            // empresas porque lleva el ProviderID dentro. La versión de C#
            // (RoleNaming.ForProvider) además quita tildes; la diferencia es irrelevante
            // porque el valor es OPACO — nada lo compara ni lo muestra, solo tiene que ser
            // único.
            //
            // El filtro NOT LIKE N'p:%' hace la migración IDEMPOTENTE: reejecutarla no
            // vuelve a prefijar lo ya prefijado.
            //
            // ⚠️ Si dos roles del MISMO proveedor produjeran el mismo nombre técnico (solo
            // posible si sus nombres visibles difieren únicamente en espacios frente a
            // guiones, p. ej. "Jefe bodega" y "Jefe-bodega"), este UPDATE viola el índice
            // único y la migración FALLA de forma ruidosa, sin dejar datos a medias. Ver el
            // SQL de verificación previa en la documentación del cambio.
            migrationBuilder.Sql(@"
UPDATE [AspNetRoles]
SET    [Name] =
           N'p:' + LOWER(REPLACE(CONVERT(nvarchar(36), [ProviderID]), N'-', N'')) + N':' +
           LOWER(REPLACE(LTRIM(RTRIM([Name])), N' ', N'-')),
       [NormalizedName] =
           N'P:' + UPPER(REPLACE(CONVERT(nvarchar(36), [ProviderID]), N'-', N'')) + N':' +
           UPPER(REPLACE(LTRIM(RTRIM([Name])), N' ', N'-'))
WHERE  [ProviderID] IS NOT NULL
  AND  [Name] IS NOT NULL
  AND  [Name] NOT LIKE N'p:%';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Devuelve a los roles de proveedor su nombre original ANTES de perder la
            // columna que lo guarda.
            //
            // ⚠️ Este paso puede fallar por violación del índice único, y es correcto que
            // falle: si dos empresas crearon un rol "Bodeguero" mientras el prefijo estaba
            // activo, revertir es literalmente imposible sin decidir cuál de las dos pierde
            // su nombre. Es el bug original reapareciendo.
            migrationBuilder.Sql(@"
UPDATE [AspNetRoles]
SET    [Name] = [DisplayName],
       [NormalizedName] = UPPER([DisplayName])
WHERE  [ProviderID] IS NOT NULL
  AND  [DisplayName] IS NOT NULL
  AND  [DisplayName] <> N''
  AND  [Name] LIKE N'p:%';
");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AspNetRoles");
        }
    }
}
