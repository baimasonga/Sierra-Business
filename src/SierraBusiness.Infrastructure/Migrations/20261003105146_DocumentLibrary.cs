using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SierraBusiness.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileBlobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileBlobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoredDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoredDocumentLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Target = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredDocumentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredDocumentLinks_StoredDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "StoredDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoredDocumentRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DetailsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Voided = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredDocumentRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredDocumentRevisions_StoredDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "StoredDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Pages = table.Column<int>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<float>(type: "REAL", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredFiles_StoredDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "StoredDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoredDocumentLinkRemovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LinkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredDocumentLinkRemovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredDocumentLinkRemovals_StoredDocumentLinks_LinkId",
                        column: x => x.LinkId,
                        principalTable: "StoredDocumentLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileBlobs_Sha256",
                table: "FileBlobs",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredDocumentLinkRemovals_LinkId",
                table: "StoredDocumentLinkRemovals",
                column: "LinkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredDocumentLinks_DocumentId",
                table: "StoredDocumentLinks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredDocumentLinks_Target_Key",
                table: "StoredDocumentLinks",
                columns: new[] { "Target", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredDocumentRevisions_DocumentId_Version",
                table: "StoredDocumentRevisions",
                columns: new[] { "DocumentId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredDocuments_Branch",
                table: "StoredDocuments",
                column: "Branch");

            migrationBuilder.CreateIndex(
                name: "IX_StoredDocuments_Number",
                table: "StoredDocuments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_DocumentId_Version",
                table: "StoredFiles",
                columns: new[] { "DocumentId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_Sha256",
                table: "StoredFiles",
                column: "Sha256");

            // Earlier "Scan & capture" documents join the library. Their originals stay where they are (StorageKey capture:ID); nothing is copied or removed.
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql("""
                    INSERT INTO StoredDocuments (Id, Branch, Number, Created, CreatedBy)
                    SELECT c.Id, c.Branch, 'DOC-CAP-' || upper(substr(replace(c.Id, '-', ''), 1, 10)), c.Created, c.Actor FROM Captures c;
                    """);
                migrationBuilder.Sql("""
                    INSERT INTO StoredDocumentRevisions (Id, DocumentId, Version, Action, DetailsJson, Voided, Reason, Actor, Time)
                    SELECT upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))), c.Id, 1, 'Imported from Scan & capture',
                        json_object(
                            'Type', CASE json_extract(r.Detail, '$.Fields.Kind') WHEN 'Tender' THEN 0 WHEN 'Invoice' THEN 2 WHEN 'Receipt' THEN 3 WHEN 'Proforma' THEN 6 ELSE 9 END,
                            'Title', substr(c.FileName, 1, 160),
                            'Reference', substr(trim(coalesce(json_extract(r.Detail, '$.Fields.Reference'), '')), 1, 120),
                            'Party', substr(trim(coalesce(json_extract(r.Detail, '$.Fields.Party'), '')), 1, 160),
                            'DocumentDate', NULL, 'Amount', NULL, 'Currency', '', 'DueDate', NULL, 'Tags', 'scan-capture',
                            'Notes', substr(trim(CASE WHEN coalesce(json_extract(r.Detail, '$.Fields.Date'), '') <> '' THEN 'Printed date: ' || json_extract(r.Detail, '$.Fields.Date') || '. ' ELSE '' END
                                || CASE WHEN coalesce(json_extract(r.Detail, '$.Fields.Total'), '') <> '' THEN 'Printed total: ' || json_extract(r.Detail, '$.Fields.Total') || '. ' ELSE '' END
                                || CASE WHEN r.Action = 'Reviewed capture' THEN 'Reviewed in capture.' ELSE '' END), 1, 200),
                            'Confidential', json('false')),
                        0, '', r.Actor, r.Created
                    FROM Captures c JOIN CaptureRevisions r ON r.CaptureId = c.Id AND r.Version = (SELECT max(x.Version) FROM CaptureRevisions x WHERE x.CaptureId = c.Id);
                    """);
                migrationBuilder.Sql("""
                    INSERT INTO StoredFiles (Id, DocumentId, Version, FileName, ContentType, Size, Sha256, Pages, Confidence, Payload, StorageKey, Reason, Actor, Created)
                    SELECT upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))), c.Id, 1, c.FileName,
                        CASE lower(substr(c.FileName, -4)) WHEN '.pdf' THEN 'application/pdf' WHEN '.png' THEN 'image/png' WHEN '.jpg' THEN 'image/jpeg' WHEN 'jpeg' THEN 'image/jpeg' WHEN '.bmp' THEN 'image/bmp' WHEN '.tif' THEN 'image/tiff' WHEN 'tiff' THEN 'image/tiff' ELSE 'application/octet-stream' END,
                        length(c.Original), c.Sha256, c.Pages, c.Confidence, c.Payload, 'capture:' || c.Id, 'Imported from Scan & capture', c.Actor, c.Created
                    FROM Captures c;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileBlobs");

            migrationBuilder.DropTable(
                name: "StoredDocumentLinkRemovals");

            migrationBuilder.DropTable(
                name: "StoredDocumentRevisions");

            migrationBuilder.DropTable(
                name: "StoredFiles");

            migrationBuilder.DropTable(
                name: "StoredDocumentLinks");

            migrationBuilder.DropTable(
                name: "StoredDocuments");
        }
    }
}
