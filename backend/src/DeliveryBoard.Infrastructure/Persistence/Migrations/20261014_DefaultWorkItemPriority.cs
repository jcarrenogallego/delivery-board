using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DefaultWorkItemPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Some legacy databases allowed missing priority. Backfill only genuine absence.
            migrationBuilder.Sql("UPDATE work_items SET \"Priority\" = 'Medium' WHERE \"Priority\" IS NULL;");
            migrationBuilder.Sql("ALTER TABLE work_items ALTER COLUMN \"Priority\" SET NOT NULL;");
            migrationBuilder.AlterColumn<string>(
                name: "Priority",
                table: "work_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Medium",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Priority",
                table: "work_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Medium");
        }
    }
}
