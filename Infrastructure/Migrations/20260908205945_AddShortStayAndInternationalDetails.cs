using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PupilAdmissions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShortStayAndInternationalDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsShortStay",
                table: "Pupils",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "InternationalDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PupilId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DepositDetail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Nationality = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternationalDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternationalDetails_Pupils_PupilId",
                        column: x => x.PupilId,
                        principalTable: "Pupils",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShortStayDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PupilId = table.Column<int>(type: "INTEGER", nullable: false),
                    LengthOfStay = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    InternationalDetailId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortStayDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortStayDetails_InternationalDetails_InternationalDetailId",
                        column: x => x.InternationalDetailId,
                        principalTable: "InternationalDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShortStayDetails_Pupils_PupilId",
                        column: x => x.PupilId,
                        principalTable: "Pupils",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InternationalDetails_PupilId",
                table: "InternationalDetails",
                column: "PupilId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayDetails_InternationalDetailId",
                table: "ShortStayDetails",
                column: "InternationalDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayDetails_PupilId",
                table: "ShortStayDetails",
                column: "PupilId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShortStayDetails");

            migrationBuilder.DropTable(
                name: "InternationalDetails");

            migrationBuilder.DropColumn(
                name: "IsShortStay",
                table: "Pupils");
        }
    }
}
