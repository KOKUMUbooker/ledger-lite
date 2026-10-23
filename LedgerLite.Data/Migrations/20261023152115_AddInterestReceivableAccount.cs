using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerLite.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInterestReceivableAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "Id", "Code", "IsActive", "Name", "ParentAccountId", "Type" },
                values: new object[] { 7, "1210", true, "Interest Receivable", null, "Asset" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 7);
        }
    }
}
