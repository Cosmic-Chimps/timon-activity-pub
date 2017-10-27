using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace Kroeg.Server.Migrations
{
    public partial class AddTripleStore : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attributes",
                columns: table => new
                {
                    AttributeId = table.Column<int>(type: "int4", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    Uri = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attributes", x => x.AttributeId);
                });

            migrationBuilder.CreateTable(
                name: "TripleEntities",
                columns: table => new
                {
                    EntityId = table.Column<int>(type: "int4", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    IdId = table.Column<int>(type: "int4", nullable: false),
                    IsOwner = table.Column<bool>(type: "bool", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: true),
                    Updated = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripleEntities", x => x.EntityId);
                    table.ForeignKey(
                        name: "FK_TripleEntities_Attributes_IdId",
                        column: x => x.IdId,
                        principalTable: "Attributes",
                        principalColumn: "AttributeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Triples",
                columns: table => new
                {
                    TripleId = table.Column<int>(type: "int4", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    Object = table.Column<string>(type: "text", nullable: true),
                    PredicateId = table.Column<int>(type: "int4", nullable: false),
                    SubjectId = table.Column<int>(type: "int4", nullable: false),
                    TypeId = table.Column<int>(type: "int4", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Triples", x => x.TripleId);
                    table.ForeignKey(
                        name: "FK_Triples_TripleEntities_PredicateId",
                        column: x => x.PredicateId,
                        principalTable: "TripleEntities",
                        principalColumn: "EntityId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Triples_Attributes_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Attributes",
                        principalColumn: "AttributeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Triples_Attributes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "Attributes",
                        principalColumn: "AttributeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripleEntities_IdId",
                table: "TripleEntities",
                column: "IdId");

            migrationBuilder.CreateIndex(
                name: "IX_Triples_PredicateId",
                table: "Triples",
                column: "PredicateId");

            migrationBuilder.CreateIndex(
                name: "IX_Triples_SubjectId",
                table: "Triples",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Triples_TypeId",
                table: "Triples",
                column: "TypeId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Triples");

            migrationBuilder.DropTable(
                name: "TripleEntities");

            migrationBuilder.DropTable(
                name: "Attributes");
        }
    }
}
