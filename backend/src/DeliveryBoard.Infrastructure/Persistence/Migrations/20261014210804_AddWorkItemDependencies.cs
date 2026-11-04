using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemDependencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "PrerequisiteIds",
                table: "work_items",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::uuid[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrerequisiteIds",
                table: "work_items");
        }
    }
}
