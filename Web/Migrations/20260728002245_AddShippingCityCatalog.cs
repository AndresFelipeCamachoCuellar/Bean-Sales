using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingCityCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "HeightCm",
                table: "Products",
                type: "decimal(6,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LengthCm",
                table: "Products",
                type: "decimal(6,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingWeightGrams",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WidthCm",
                table: "Products",
                type: "decimal(6,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingCarrier",
                table: "Orders",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingCity",
                table: "Orders",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingCityDaneCode",
                table: "Orders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingEstimatedDays",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingQuoteSource",
                table: "Orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShippingCities",
                columns: table => new
                {
                    ShippingCityID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DaneCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    MipaqueteLocationCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Status = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingCities", x => x.ShippingCityID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingCities_DaneCode",
                table: "ShippingCities",
                column: "DaneCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShippingCities");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LengthCm",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShippingWeightGrams",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "WidthCm",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShippingCarrier",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingCity",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingCityDaneCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingEstimatedDays",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingQuoteSource",
                table: "Orders");
        }
    }
}
