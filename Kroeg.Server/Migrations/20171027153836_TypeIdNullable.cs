using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace Kroeg.Server.Migrations
{
    public partial class TypeIdNullable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Triples_Attributes_TypeId",
                table: "Triples");

            migrationBuilder.AlterColumn<int>(
                name: "TypeId",
                table: "Triples",
                type: "int4",
                nullable: true,
                oldClrType: typeof(int));

            migrationBuilder.AddForeignKey(
                name: "FK_Triples_Attributes_TypeId",
                table: "Triples",
                column: "TypeId",
                principalTable: "Attributes",
                principalColumn: "AttributeId",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Triples_Attributes_TypeId",
                table: "Triples");

            migrationBuilder.AlterColumn<int>(
                name: "TypeId",
                table: "Triples",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int4",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Triples_Attributes_TypeId",
                table: "Triples",
                column: "TypeId",
                principalTable: "Attributes",
                principalColumn: "AttributeId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
