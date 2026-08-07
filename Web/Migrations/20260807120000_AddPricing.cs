using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web.Migrations
{
    /// <summary>
    /// E2 — Precio de venta y margen. Separa el COSTO del proveedor
    /// (<c>Products.SupplierPrice</c>) del PVP con el que vende Bean (<c>Products.Price</c>),
    /// agrega el histórico de cambios y la configuración de márgenes.
    ///
    /// ⚠️ Incluye BACKFILL de datos, no solo esquema. Ver el bloque marcado abajo.
    /// </summary>
    public partial class AddPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------------------------------------------------------------- Esquema

            migrationBuilder.AddColumn<bool>(
                name: "MarginAlert",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PriceSetAt",
                table: "Products",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceSetBy",
                table: "Products",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierPrice",
                table: "Products",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierPriceSnapshot",
                table: "OrderItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "PriceChangeLogs",
                columns: table => new
                {
                    PriceChangeLogID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeType = table.Column<int>(type: "int", nullable: false),
                    OldValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NewValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MarginBefore = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    MarginAfter = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ChangedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    NotifiedProvider = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceChangeLogs", x => x.PriceChangeLogID);
                    table.ForeignKey(
                        name: "FK_PriceChangeLogs_Products_ProductID",
                        column: x => x.ProductID,
                        principalTable: "Products",
                        principalColumn: "ProductID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PricingSettings",
                columns: table => new
                {
                    PricingSettingsID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetMarginPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    MinimumMarginPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    RoundingStep = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingSettings", x => x.PricingSettingsID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_MarginAlert",
                table: "Products",
                column: "MarginAlert");

            migrationBuilder.CreateIndex(
                name: "IX_PriceChangeLogs_ProductID_ChangedOn",
                table: "PriceChangeLogs",
                columns: new[] { "ProductID", "ChangedOn" });

            // ================================================================
            //  BACKFILL OBLIGATORIO — no es opcional ni cosmético
            // ================================================================
            // Hasta hoy `Products.Price` ERA el precio del proveedor: Bean vendía al costo
            // y no ganaba nada. Sin este backfill los productos existentes quedarían con
            // SupplierPrice = 0 y un margen ficticio del 100 %, que es exactamente la
            // mentira que esta épica viene a eliminar.
            //
            // MarginAlert = 1 en todas las filas a propósito: con costo == PVP el margen es
            // 0 %, por debajo del mínimo (15 %). Todas tienen que pasar por la bandeja
            // "Márgenes por revisar" y recibir un PVP a mano.
            migrationBuilder.Sql(@"
                UPDATE [Products]
                   SET [SupplierPrice] = [Price],
                       [MarginAlert]   = 1;");

            // Las líneas de pedidos históricos se congelan con costo == PVP: es la verdad
            // contable de esas ventas (Bean no tuvo margen). Deja E3 lista para liquidar
            // sin inventar cifras.
            migrationBuilder.Sql(@"
                UPDATE [OrderItems]
                   SET [SupplierPriceSnapshot] = [UnitPrice];");

            // Fila única de configuración. Valores decididos por el PO (07-ago-2026):
            // objetivo 30 %, mínimo 15 % (PISO que dispara la alerta, no la meta),
            // redondeo del precio sugerido a 50 COP.
            // El GUID es el mismo que PricingSettings.SingletonId.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM [PricingSettings])
                BEGIN
                    INSERT INTO [PricingSettings]
                        ([PricingSettingsID], [TargetMarginPercent], [MinimumMarginPercent],
                         [RoundingStep], [Status], [CreatedBy], [CreatedOn])
                    VALUES
                        ('9E1C4B2A-0000-4000-8000-BEA000000001', 30.00, 15.00,
                         50.00, 1, 'SYSTEM', SYSDATETIME());
                END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceChangeLogs");

            migrationBuilder.DropTable(
                name: "PricingSettings");

            migrationBuilder.DropIndex(
                name: "IX_Products_MarginAlert",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "MarginAlert",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PriceSetAt",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PriceSetBy",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SupplierPrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SupplierPriceSnapshot",
                table: "OrderItems");
        }
    }
}
