using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "work_items",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Comment",
                table: "work_items");
        }
    }
}
