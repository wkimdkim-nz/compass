using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Compass.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectionsReplaceNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notes");

            migrationBuilder.CreateTable(
                name: "projections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cik = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projections", x => x.id);
                    table.ForeignKey(
                        name: "fk_projections_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scenarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    projection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    growth_rate = table.Column<decimal>(type: "numeric", nullable: false),
                    future_net_margin = table.Column<decimal>(type: "numeric", nullable: false),
                    exit_multiple_kind = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    exit_multiple_value = table.Column<decimal>(type: "numeric", nullable: false),
                    dilution_rate = table.Column<decimal>(type: "numeric", nullable: false),
                    horizon = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scenarios", x => x.id);
                    table.ForeignKey(
                        name: "fk_scenarios_projections_projection_id",
                        column: x => x.projection_id,
                        principalTable: "projections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_projections_user_id_ticker",
                table: "projections",
                columns: new[] { "user_id", "ticker" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scenarios_projection_id_name",
                table: "scenarios",
                columns: new[] { "projection_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scenarios");

            migrationBuilder.DropTable(
                name: "projections");

            migrationBuilder.CreateTable(
                name: "notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_notes_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notes_user_id",
                table: "notes",
                column: "user_id");
        }
    }
}
