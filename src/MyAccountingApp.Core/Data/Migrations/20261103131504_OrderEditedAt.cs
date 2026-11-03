using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyAccountingApp.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrderEditedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "edited_at",
                table: "orders",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "edited_at",
                table: "orders");
        }
    }
}
