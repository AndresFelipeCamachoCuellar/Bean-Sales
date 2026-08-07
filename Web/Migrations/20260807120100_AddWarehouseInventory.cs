using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web.Migrations
{
    /// <summary>
    /// E1 — Inventario multi-bodega. <c>Product.Stock</c> deja de ser la verdad y pasa a
    /// ser un total denormalizado; la verdad contable es el libro de <c>StockMovements</c>,
    /// con el saldo por producto × bodega en <c>StockItems</c>.
    ///
    /// ⚠️ Incluye SEED de la bodega <c>CAL-01</c> y BACKFILL del stock actual. Ver el bloque
    /// marcado abajo.
    ///
    /// Debe aplicarse DESPUÉS de <c>AddPricing</c>: el backfill del libro mayor usa
    /// <c>Products.SupplierPrice</c> como costo unitario del asiento inicial.
    /// </summary>
    public partial class AddWarehouseInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------------------------------------------------------------- Esquema

            migrationBuilder.AddColumn<Guid>(
                name: "FulfillmentWarehouseID",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseID",
                table: "OrderItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    WarehouseID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CountryID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DaneCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.WarehouseID);
                    // Restrict: un país con bodegas no se borra en silencio.
                    table.ForeignKey(
                        name: "FK_Warehouses_Countries_CountryID",
                        column: x => x.CountryID,
                        principalTable: "Countries",
                        principalColumn: "CountryID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockItems",
                columns: table => new
                {
                    ProductID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityOnHand = table.Column<int>(type: "int", nullable: false),
                    QuantityReserved = table.Column<int>(type: "int", nullable: false),
                    Ownership = table.Column<int>(type: "int", nullable: false),
                    ReorderPoint = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Status = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockItems", x => new { x.ProductID, x.WarehouseID });
                    // ⚠️ Restrict en AMBAS ramas: Product y Warehouse llegan los dos a
                    // StockItems y SQL Server rechazaría los "multiple cascade paths".
                    // Mismo criterio que Order→User y OrderItem→Product.
                    table.ForeignKey(
                        name: "FK_StockItems_Products_ProductID",
                        column: x => x.ProductID,
                        principalTable: "Products",
                        principalColumn: "ProductID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockItems_Warehouses_WarehouseID",
                        column: x => x.WarehouseID,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    StockMovementID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementType = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ReferenceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReferenceID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.StockMovementID);
                    // Un asiento contable NO se borra en cascada. Nunca.
                    table.ForeignKey(
                        name: "FK_StockMovements_Products_ProductID",
                        column: x => x.ProductID,
                        principalTable: "Products",
                        principalColumn: "ProductID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Warehouses_WarehouseID",
                        column: x => x.WarehouseID,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_WarehouseID",
                table: "StockItems",
                column: "WarehouseID");

            // El historial se consulta SIEMPRE por producto × bodega y por fecha. El hosting
            // tiene 256 MB y esta tabla crece sin techo: sin índice haría table scan.
            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductID_WarehouseID_CreatedOn",
                table: "StockMovements",
                columns: new[] { "ProductID", "WarehouseID", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ReferenceType_ReferenceID",
                table: "StockMovements",
                columns: new[] { "ReferenceType", "ReferenceID" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_WarehouseID",
                table: "StockMovements",
                column: "WarehouseID");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_CountryID",
                table: "Warehouses",
                column: "CountryID");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_DaneCode",
                table: "Warehouses",
                column: "DaneCode");

            // ================================================================
            //  SEED + BACKFILL — un solo lote de T-SQL (el DECLARE no cruza batches)
            // ================================================================
            // El GUID de la bodega es el mismo que Warehouse.DefaultWarehouseId.
            //
            // ⚠️ DANE de Cali = 76001. 760001 es el código POSTAL. Ya se confundieron una
            // vez en este proyecto; no repetir.
            //
            // El seed depende de que exista el país "Colombia". En una base NUEVA las
            // migraciones corren ANTES de ContextSeed, así que Countries está vacía y este
            // bloque no hace nada: la red de seguridad es ContextSeed.SeedDefaultWarehouseAsync,
            // que crea la misma bodega (mismo GUID) justo después de sembrar los países.
            migrationBuilder.Sql(@"
                DECLARE @WarehouseId uniqueidentifier = 'CA1D0001-0000-4000-8000-BEA000000001';
                DECLARE @ColombiaId  uniqueidentifier = (SELECT TOP 1 [CountryID] FROM [Countries] WHERE [Name] = N'Colombia');

                IF @ColombiaId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [Warehouses])
                BEGIN
                    INSERT INTO [Warehouses]
                        ([WarehouseID], [Code], [Name], [CountryID], [City], [DaneCode], [Address],
                         [IsDefault], [IsActive], [Status], [CreatedBy], [CreatedOn])
                    VALUES
                        (@WarehouseId, N'CAL-01', N'Bodega Principal Cali', @ColombiaId, N'Cali', N'76001',
                         N'Por definir', 1, 1, 1, N'MIGRATION', SYSDATETIME());
                END

                IF EXISTS (SELECT 1 FROM [Warehouses] WHERE [WarehouseID] = @WarehouseId)
                BEGIN
                    -- Saldo inicial: todo el stock global de cada producto entra a CAL-01.
                    -- RowVersion NO se inserta: SQL Server lo genera.
                    INSERT INTO [StockItems]
                        ([ProductID], [WarehouseID], [QuantityOnHand], [QuantityReserved],
                         [Ownership], [ReorderPoint], [Status], [CreatedBy], [CreatedOn])
                    SELECT p.[ProductID], @WarehouseId, p.[Stock], 0,
                           0, NULL, 1, N'MIGRATION', SYSDATETIME()
                      FROM [Products] p
                     WHERE p.[Stock] > 0
                       AND NOT EXISTS (SELECT 1 FROM [StockItems] s
                                        WHERE s.[ProductID] = p.[ProductID]
                                          AND s.[WarehouseID] = @WarehouseId);

                    -- Asiento de apertura del libro mayor. Tipo 4 = Adjustment.
                    -- UnitCost sale de SupplierPrice, que AddPricing acaba de rellenar.
                    INSERT INTO [StockMovements]
                        ([StockMovementID], [ProductID], [WarehouseID], [MovementType], [Quantity],
                         [ReferenceType], [ReferenceID], [Reason], [UnitCost], [CreatedBy], [CreatedOn])
                    SELECT NEWID(), p.[ProductID], @WarehouseId, 4, p.[Stock],
                           N'Migration', NULL, N'Migración inicial de stock global a bodega',
                           p.[SupplierPrice], N'MIGRATION', SYSDATETIME()
                      FROM [Products] p
                     WHERE p.[Stock] > 0
                       AND NOT EXISTS (SELECT 1 FROM [StockMovements] m
                                        WHERE m.[ProductID] = p.[ProductID]
                                          AND m.[WarehouseID] = @WarehouseId
                                          AND m.[ReferenceType] = N'Migration');
                END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockItems");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropColumn(
                name: "FulfillmentWarehouseID",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "WarehouseID",
                table: "OrderItems");
        }
    }
}
