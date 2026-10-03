using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomerWarrantyMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    StartsAt = table.Column<int>(type: "INTEGER", nullable: false),
                    SupplierWarrantyMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiresInstallation = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChecklistJson = table.Column<string>(type: "TEXT", nullable: false),
                    Coverage = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentTerms_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Installations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Customer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Site = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Scheduled = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Engineer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Installations_Documents_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EquipmentProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductLinks_Products_EquipmentProductId",
                        column: x => x.EquipmentProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductLinks_Products_ItemProductId",
                        column: x => x.ItemProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Serial = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    SpecificationsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Accessories = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Warranties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Serial = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Customer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Start = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    End = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Months = table.Column<int>(type: "INTEGER", nullable: false),
                    Coverage = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warranties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Warranties_Documents_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InstallationCancellations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationCancellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallationCancellations_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InstallationCompletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Completed = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ChecklistDoneJson = table.Column<string>(type: "TEXT", nullable: false),
                    AcceptedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TrainingGivenTo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallationCompletions_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InstallationUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Serial = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ChecklistJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallationUnits_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductLinkRemovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LinkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductLinkRemovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductLinkRemovals_ProductLinks_LinkId",
                        column: x => x.LinkId,
                        principalTable: "ProductLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentTerms_ProductId_Version",
                table: "EquipmentTerms",
                columns: new[] { "ProductId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallationCancellations_InstallationId",
                table: "InstallationCancellations",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallationCompletions_InstallationId",
                table: "InstallationCompletions",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Installations_Number",
                table: "Installations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Installations_SaleId",
                table: "Installations",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_InstallationUnits_InstallationId_ProductId_Serial",
                table: "InstallationUnits",
                columns: new[] { "InstallationId", "ProductId", "Serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallationUnits_Serial",
                table: "InstallationUnits",
                column: "Serial");

            migrationBuilder.CreateIndex(
                name: "IX_ProductLinkRemovals_LinkId",
                table: "ProductLinkRemovals",
                column: "LinkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductLinks_EquipmentProductId",
                table: "ProductLinks",
                column: "EquipmentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductLinks_ItemProductId",
                table: "ProductLinks",
                column: "ItemProductId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitProfiles_ProductId_Serial_Version",
                table: "UnitProfiles",
                columns: new[] { "ProductId", "Serial", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warranties_SaleId_ProductId_Serial",
                table: "Warranties",
                columns: new[] { "SaleId", "ProductId", "Serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warranties_Serial",
                table: "Warranties",
                column: "Serial");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentTerms");

            migrationBuilder.DropTable(
                name: "InstallationCancellations");

            migrationBuilder.DropTable(
                name: "InstallationCompletions");

            migrationBuilder.DropTable(
                name: "InstallationUnits");

            migrationBuilder.DropTable(
                name: "ProductLinkRemovals");

            migrationBuilder.DropTable(
                name: "UnitProfiles");

            migrationBuilder.DropTable(
                name: "Warranties");

            migrationBuilder.DropTable(
                name: "Installations");

            migrationBuilder.DropTable(
                name: "ProductLinks");
        }
    }
}
