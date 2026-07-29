using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web.Migrations
{
    /// <inheritdoc />
    public partial class AddWompiPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PaymentAmountInCents",
                table: "Orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentAttempt",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PaymentEnvironment",
                table: "Orders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentExpiresAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethodUsed",
                table: "Orders",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentStatus",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatusMessage",
                table: "Orders",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTransactionId",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentReference",
                table: "Orders",
                column: "PaymentReference",
                unique: true,
                filter: "[PaymentReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentStatus",
                table: "Orders",
                column: "PaymentStatus");

            // Backfill de pedidos históricos: se crearon ANTES de existir la pasarela,
            // así que en la práctica ya estaban "pagados" (el checkout los confirmaba
            // directo). Sin esto quedarían como PaymentStatus = Pending (0) y el
            // Dashboard reportaría $0 en ventas y "Mis pedidos" los mostraría como
            // pago no completado. Se excluyen los cancelados (OrderStatus = 5).
            // PaymentStatus = 1 => Approved.
            migrationBuilder.Sql(@"
                UPDATE [Orders]
                   SET [PaymentStatus] = 1,
                       [PaidAt] = [OrderDate]
                 WHERE [OrderStatus] <> 5;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentReference",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentAmountInCents",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentAttempt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentEnvironment",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentExpiresAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentMethodUsed",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentStatusMessage",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentTransactionId",
                table: "Orders");
        }
    }
}
