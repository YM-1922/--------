using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Doctor.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddInstaPayAndItemDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentReferenceNumber",
                table: "Sales",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemDetails",
                table: "SaleItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalInstaPaySales",
                table: "CashShifts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalVisaSales",
                table: "CashShifts",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentReferenceNumber",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ItemDetails",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "TotalInstaPaySales",
                table: "CashShifts");

            migrationBuilder.DropColumn(
                name: "TotalVisaSales",
                table: "CashShifts");
        }
    }
}
