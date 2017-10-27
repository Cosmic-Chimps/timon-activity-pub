using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace Kroeg.Server.Migrations
{
    public partial class FineTuneTripleStore : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Triples_TripleEntities_PredicateId",
                table: "Triples");

            migrationBuilder.AddColumn<int>(
                name: "AttributeId",
                table: "Triples",
                type: "int4",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubjectEntityId",
                table: "Triples",
                type: "int4",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Triples_AttributeId",
                table: "Triples",
                column: "AttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_Triples_SubjectEntityId",
                table: "Triples",
                column: "SubjectEntityId");

            migrationBuilder.AddForeignKey(
                name: "FK_Triples_Attributes_AttributeId",
                table: "Triples",
                column: "AttributeId",
                principalTable: "Attributes",
                principalColumn: "AttributeId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Triples_Attributes_PredicateId",
                table: "Triples",
                column: "PredicateId",
                principalTable: "Attributes",
                principalColumn: "AttributeId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Triples_TripleEntities_SubjectEntityId",
                table: "Triples",
                column: "SubjectEntityId",
                principalTable: "TripleEntities",
                principalColumn: "EntityId",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Triples_Attributes_AttributeId",
                table: "Triples");

            migrationBuilder.DropForeignKey(
                name: "FK_Triples_Attributes_PredicateId",
                table: "Triples");

            migrationBuilder.DropForeignKey(
                name: "FK_Triples_TripleEntities_SubjectEntityId",
                table: "Triples");

            migrationBuilder.DropIndex(
                name: "IX_Triples_AttributeId",
                table: "Triples");

            migrationBuilder.DropIndex(
                name: "IX_Triples_SubjectEntityId",
                table: "Triples");

            migrationBuilder.DropColumn(
                name: "AttributeId",
                table: "Triples");

            migrationBuilder.DropColumn(
                name: "SubjectEntityId",
                table: "Triples");

            migrationBuilder.AddForeignKey(
                name: "FK_Triples_TripleEntities_PredicateId",
                table: "Triples",
                column: "PredicateId",
                principalTable: "TripleEntities",
                principalColumn: "EntityId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
