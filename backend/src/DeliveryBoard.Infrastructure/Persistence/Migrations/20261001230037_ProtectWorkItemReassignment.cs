using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryBoard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectWorkItemReassignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Owner and Status already exist; concurrency protection only changes EF metadata.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // There are no database schema changes to revert.
        }
    }
}
