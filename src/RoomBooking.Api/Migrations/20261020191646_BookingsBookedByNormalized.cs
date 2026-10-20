using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomBooking.Api.Migrations
{
    /// <inheritdoc />
    public partial class BookingsBookedByNormalized : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Istniejące wiersze dostają "" i są uzupełniane w kodzie przy starcie
            // (DatabaseInitializer): SQLite upper() zmienia tylko ASCII, więc nie obsłużyłby polskich liter.
            migrationBuilder.AddColumn<string>(
                name: "BookedByNormalized",
                table: "Bookings",
                type: "TEXT",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookedByNormalized",
                table: "Bookings",
                column: "BookedByNormalized");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_BookedByNormalized",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "BookedByNormalized",
                table: "Bookings");
        }
    }
}
