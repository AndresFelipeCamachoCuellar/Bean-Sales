using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web.Migrations
{
    /// <summary>
    /// Migración de SINCRONIZACIÓN, intencionalmente vacía.
    ///
    /// El snapshot del modelo (ApplicationDbContextModelSnapshot) se revirtió al reescribir
    /// el historial de git, mientras que la base de datos YA tenía aplicados el catálogo de
    /// ciudades y los campos de envío (migraciones AddShippingCityCatalog y
    /// AddDynamicShippingFields).
    ///
    /// Al generar esta migración, EF regeneró el snapshot correctamente, pero además emitió
    /// las operaciones de esquema que ya existían en la base. Por eso Up() y Down() quedan
    /// SIN operaciones: aplicarla solo registra la migración en __EFMigrationsHistory y deja
    /// el snapshot alineado con el modelo, sin tocar el esquema.
    /// </summary>
    public partial class SyncShippingSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intencionalmente vacío: el esquema ya está aplicado en la base de datos.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intencionalmente vacío.
        }
    }
}
