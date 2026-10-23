using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerLite.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLoans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Loans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PrincipalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AnnualRatePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TermMonths = table.Column<int>(type: "int", nullable: false),
                    DisbursedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    PrincipalAccountId = table.Column<int>(type: "int", nullable: false),
                    InterestAccountId = table.Column<int>(type: "int", nullable: false),
                    DisbursementKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Loans_Accounts_InterestAccountId",
                        column: x => x.InterestAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Loans_Accounts_PrincipalAccountId",
                        column: x => x.PrincipalAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Loans_DisbursementKey",
                table: "Loans",
                column: "DisbursementKey",
                unique: true,
                filter: "[DisbursementKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_InterestAccountId",
                table: "Loans",
                column: "InterestAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_PrincipalAccountId",
                table: "Loans",
                column: "PrincipalAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Loans");
        }
    }
}
