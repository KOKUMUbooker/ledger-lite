using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerLite.Data.Migrations
{
    /// <inheritdoc />
    public partial class BlockLedgerMutation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_JournalLines_Immutable ON JournalLines
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 50001, 'Journal lines are immutable. Post a reversing entry instead.', 1;
END");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_JournalEntries_Immutable ON JournalEntries
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 50001, 'Journal entries are immutable. Post a reversing entry instead.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER TR_JournalEntries_Immutable");
            migrationBuilder.Sql("DROP TRIGGER TR_JournalLines_Immutable");
        }
    }
}