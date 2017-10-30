using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace Kroeg.Server.Migrations
{
    public partial class RedoMigrations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int4", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bool", nullable: false),
                    LockoutEnabled = table.Column<bool>(type: "bool", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    NormalizedEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bool", nullable: false),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    TwoFactorEnabled = table.Column<bool>(type: "bool", nullable: false),
                    UserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

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
                name: "EventQueue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int4", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    Action = table.Column<string>(type: "text", nullable: true),
                    Added = table.Column<DateTime>(type: "timestamp", nullable: false),
                    AttemptCount = table.Column<int>(type: "int4", nullable: false),
                    Data = table.Column<string>(type: "text", nullable: true),
                    NextAttempt = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventQueue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int4", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int4", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "CollectionItems",
                columns: table => new
                {
                    CollectionItemId = table.Column<int>(type: "int4", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    CollectionId = table.Column<int>(type: "int4", nullable: false),
                    ElementId = table.Column<int>(type: "int4", nullable: false),
                    IsPublic = table.Column<bool>(type: "bool", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionItems", x => x.CollectionItemId);
                    table.ForeignKey(
                        name: "FK_CollectionItems_TripleEntities_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "TripleEntities",
                        principalColumn: "EntityId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionItems_TripleEntities_ElementId",
                        column: x => x.ElementId,
                        principalTable: "TripleEntities",
                        principalColumn: "EntityId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JsonWebKeys",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OwnerId = table.Column<int>(type: "int4", nullable: false),
                    SerializedData = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JsonWebKeys", x => new { x.Id, x.OwnerId });
                    table.ForeignKey(
                        name: "FK_JsonWebKeys_TripleEntities_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "TripleEntities",
                        principalColumn: "EntityId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalmonKeys",
                columns: table => new
                {
                    SalmonKeyId = table.Column<int>(type: "int4", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    EntityId = table.Column<int>(type: "int4", nullable: false),
                    PrivateKey = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalmonKeys", x => x.SalmonKeyId);
                    table.ForeignKey(
                        name: "FK_SalmonKeys_TripleEntities_EntityId",
                        column: x => x.EntityId,
                        principalTable: "TripleEntities",
                        principalColumn: "EntityId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Triples",
                columns: table => new
                {
                    TripleId = table.Column<int>(type: "int4", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    AttributeId = table.Column<int>(type: "int4", nullable: true),
                    Object = table.Column<string>(type: "text", nullable: true),
                    PredicateId = table.Column<int>(type: "int4", nullable: false),
                    SubjectEntityId = table.Column<int>(type: "int4", nullable: false),
                    SubjectId = table.Column<int>(type: "int4", nullable: false),
                    TypeId = table.Column<int>(type: "int4", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Triples", x => x.TripleId);
                    table.ForeignKey(
                        name: "FK_Triples_Attributes_AttributeId",
                        column: x => x.AttributeId,
                        principalTable: "Attributes",
                        principalColumn: "AttributeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Triples_Attributes_PredicateId",
                        column: x => x.PredicateId,
                        principalTable: "Attributes",
                        principalColumn: "AttributeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Triples_TripleEntities_SubjectEntityId",
                        column: x => x.SubjectEntityId,
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
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserActorPermissions",
                columns: table => new
                {
                    UserActorPermissionId = table.Column<int>(type: "int4", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    ActorId = table.Column<int>(type: "int4", nullable: false),
                    IsAdmin = table.Column<bool>(type: "bool", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserActorPermissions", x => x.UserActorPermissionId);
                    table.ForeignKey(
                        name: "FK_UserActorPermissions_TripleEntities_ActorId",
                        column: x => x.ActorId,
                        principalTable: "TripleEntities",
                        principalColumn: "EntityId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserActorPermissions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebSubClients",
                columns: table => new
                {
                    WebSubClientId = table.Column<int>(type: "int4", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    Expiry = table.Column<DateTime>(type: "timestamp", nullable: false),
                    ForUserId = table.Column<int>(type: "int4", nullable: false),
                    Secret = table.Column<string>(type: "text", nullable: true),
                    TargetUserId = table.Column<int>(type: "int4", nullable: false),
                    Topic = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebSubClients", x => x.WebSubClientId);
                    table.ForeignKey(
                        name: "FK_WebSubClients_TripleEntities_ForUserId",
                        column: x => x.ForUserId,
                        principalTable: "TripleEntities",
                        principalColumn: "EntityId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WebSubClients_TripleEntities_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "TripleEntities",
                        principalColumn: "EntityId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebsubSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int4", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    Callback = table.Column<string>(type: "text", nullable: true),
                    Expiry = table.Column<DateTime>(type: "timestamp", nullable: false),
                    Secret = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<int>(type: "int4", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsubSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebsubSubscriptions_TripleEntities_UserId",
                        column: x => x.UserId,
                        principalTable: "TripleEntities",
                        principalColumn: "EntityId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_CollectionId",
                table: "CollectionItems",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_ElementId",
                table: "CollectionItems",
                column: "ElementId");

            migrationBuilder.CreateIndex(
                name: "IX_JsonWebKeys_OwnerId",
                table: "JsonWebKeys",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalmonKeys_EntityId",
                table: "SalmonKeys",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_TripleEntities_IdId",
                table: "TripleEntities",
                column: "IdId");

            migrationBuilder.CreateIndex(
                name: "IX_Triples_AttributeId",
                table: "Triples",
                column: "AttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_Triples_PredicateId",
                table: "Triples",
                column: "PredicateId");

            migrationBuilder.CreateIndex(
                name: "IX_Triples_SubjectEntityId",
                table: "Triples",
                column: "SubjectEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Triples_SubjectId",
                table: "Triples",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Triples_TypeId",
                table: "Triples",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserActorPermissions_ActorId",
                table: "UserActorPermissions",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_UserActorPermissions_UserId",
                table: "UserActorPermissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WebSubClients_ForUserId",
                table: "WebSubClients",
                column: "ForUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WebSubClients_TargetUserId",
                table: "WebSubClients",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WebsubSubscriptions_UserId",
                table: "WebsubSubscriptions",
                column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "CollectionItems");

            migrationBuilder.DropTable(
                name: "EventQueue");

            migrationBuilder.DropTable(
                name: "JsonWebKeys");

            migrationBuilder.DropTable(
                name: "SalmonKeys");

            migrationBuilder.DropTable(
                name: "Triples");

            migrationBuilder.DropTable(
                name: "UserActorPermissions");

            migrationBuilder.DropTable(
                name: "WebSubClients");

            migrationBuilder.DropTable(
                name: "WebsubSubscriptions");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "TripleEntities");

            migrationBuilder.DropTable(
                name: "Attributes");
        }
    }
}
