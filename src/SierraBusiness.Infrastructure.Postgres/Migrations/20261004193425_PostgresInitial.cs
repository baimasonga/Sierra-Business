using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SierraBusiness.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PostgresInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sierra");

            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TriggersJson = table.Column<string>(type: "text", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SecurityStamp = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankFormats",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Bank = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FormatJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankFormats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bundles",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    ItemsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bundles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Captures",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Original = table.Column<byte[]>(type: "bytea", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Pages = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<float>(type: "real", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Captures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanyProfiles",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ProfileJson = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ControlPolicies",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    PolicyJson = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Party = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Settlement = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    Reverses = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Documents_Reverses",
                        column: x => x.Reverses,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTemplates",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TemplateJson = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentThumbnails",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentThumbnails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BasicSalary = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Allowances = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Deductions = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Tin = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    NassitNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    NonResident = table.Column<bool>(type: "boolean", nullable: false),
                    NassitExempt = table.Column<bool>(type: "boolean", nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BankAccount = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileBlobs",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileBlobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HeldSales",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Cashier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeldSales", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileSha256 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sheet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HeadersJson = table.Column<string>(type: "text", nullable: false),
                    MappingJson = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AsAt = table.Column<DateOnly>(type: "date", nullable: true),
                    SessionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CommittedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Committed = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Errors = table.Column<int>(type: "integer", nullable: false),
                    Warnings = table.Column<int>(type: "integer", nullable: false),
                    SummaryJson = table.Column<string>(type: "text", nullable: false),
                    DocumentsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegritySeals",
                schema: "sierra",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PreviousHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rows = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegritySeals", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "InventoryRevisions",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobRuns",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Job = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Started = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Finished = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobStates",
                schema: "sierra",
                columns: table => new
                {
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Job = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastRun = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccess = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextDue = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    DeadLettered = table.Column<bool>(type: "boolean", nullable: false),
                    LastMessage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Checkpoint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobStates", x => new { x.Branch, x.Job });
                });

            migrationBuilder.CreateTable(
                name: "LedgerAccounts",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Group = table.Column<int>(type: "integer", nullable: false),
                    CashOrBank = table.Column<bool>(type: "boolean", nullable: false),
                    Control = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaintenancePlans",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Customer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Site = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Serial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IntervalMonths = table.Column<int>(type: "integer", nullable: false),
                    FirstDue = table.Column<DateOnly>(type: "date", nullable: false),
                    ChecklistJson = table.Column<string>(type: "text", nullable: false),
                    Engineer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Operations",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Acknowledged = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Parties",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PaymentDays = table.Column<int>(type: "integer", nullable: false),
                    CreditLimit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CreditHold = table.Column<bool>(type: "boolean", nullable: false),
                    Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactsJson = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRules",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RulesJson = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PeriodClosures",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Through = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodClosures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PriceRules",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerCategory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MinQuantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Tracking = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reminders",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    Due = table.Column<DateOnly>(type: "date", nullable: true),
                    Permission = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Page = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reminders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesTaxRates",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesTaxRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockLocations",
                schema: "sierra",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLocations", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "StoredDocuments",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncChanges",
                schema: "sierra",
                columns: table => new
                {
                    Seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Commit = table.Column<Guid>(type: "uuid", nullable: false),
                    Entity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Origin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncChanges", x => x.Seq);
                });

            migrationBuilder.CreateTable(
                name: "SyncConflicts",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Origin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Entity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IncomingJson = table.Column<string>(type: "text", nullable: false),
                    Reviewed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncConflicts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncPeers",
                schema: "sierra",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Pushed = table.Column<long>(type: "bigint", nullable: false),
                    Pulled = table.Column<long>(type: "bigint", nullable: false),
                    LastAttempt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccess = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncPeers", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "Tenders",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Institution = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Officer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Closing = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosingTime = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubmissionMethod = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TillAssignments",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccountCode = table.Column<int>(type: "integer", nullable: false),
                    Cashier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeOrders",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Party = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    QuotationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeOrders_TradeOrders_QuotationId",
                        column: x => x.QuotationId,
                        principalSchema: "sierra",
                        principalTable: "TradeOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitProfiles",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Serial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SpecificationsJson = table.Column<string>(type: "text", nullable: false),
                    Accessories = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UpdateStatuses",
                schema: "sierra",
                columns: table => new
                {
                    Branch = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Checked = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Latest = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NotesJson = table.Column<string>(type: "text", nullable: false),
                    Problem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpdateStatuses", x => x.Branch);
                });

            migrationBuilder.CreateTable(
                name: "VariantGroups",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AttributesJson = table.Column<string>(type: "text", nullable: false),
                    NeedsAssembly = table.Column<bool>(type: "boolean", nullable: false),
                    NeedsDelivery = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariantGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YearEndClosures",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    YearEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Profit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndClosures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDecisions",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SelfApproved = table.Column<bool>(type: "boolean", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalDecisions_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "sierra",
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalNotes",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalNotes_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "sierra",
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalUses",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalUses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalUses_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "sierra",
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClaimType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ClaimValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "sierra",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClaimType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ClaimValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "sierra",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "sierra",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "sierra",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "sierra",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RoleId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "sierra",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "sierra",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "sierra",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LoginProvider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "sierra",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CaptureRevisions",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaptureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaptureRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaptureRevisions_Captures_CaptureId",
                        column: x => x.CaptureId,
                        principalSchema: "sierra",
                        principalTable: "Captures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillAllocations",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VoucherId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillAllocations_Documents_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillAllocations_Documents_VoucherId",
                        column: x => x.VoucherId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerCredits",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerCredits_Documents_ReturnId",
                        column: x => x.ReturnId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryInvoices",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryInvoices_Documents_DeliveryId",
                        column: x => x.DeliveryId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryInvoices_Documents_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeLoans",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Principal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Installment = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLoans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeLoans_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FiscalSubmissions",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Connector = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalSubmissions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ForeignBills",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    SourceAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForeignBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForeignBills_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ForeignSettlements",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillId = table.Column<Guid>(type: "uuid", nullable: false),
                    VoucherId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CarryingAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForeignSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForeignSettlements_Documents_BillId",
                        column: x => x.BillId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ForeignSettlements_Documents_VoucherId",
                        column: x => x.VoucherId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Installations",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Customer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Site = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Scheduled = table.Column<DateOnly>(type: "date", nullable: false),
                    Engineer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Installations_Documents_SaleId",
                        column: x => x.SaleId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IssuedCheques",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BankAccount = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payee = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    VoucherId = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedCheques", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssuedCheques_Documents_VoucherId",
                        column: x => x.VoucherId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Journal",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Account = table.Column<int>(type: "integer", nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CostCentre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Journal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Journal_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivedCheques",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Party = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Bank = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChequeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivedCheques", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivedCheques_Documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnAllocations",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Serial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Batch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Net = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Tax = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    BillCredit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnAllocations_Documents_OriginalDocumentId",
                        column: x => x.OriginalDocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnAllocations_Documents_ReturnDocumentId",
                        column: x => x.ReturnDocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StatutoryRemittances",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryRemittances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryRemittances_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCredits",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierCredits_Documents_ReturnId",
                        column: x => x.ReturnId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Warranties",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Serial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Customer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    Coverage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warranties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Warranties_Documents_SaleId",
                        column: x => x.SaleId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollItems",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollItems_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollItems_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "sierra",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HeldSaleEnds",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HeldSaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeldSaleEnds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HeldSaleEnds_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HeldSaleEnds_HeldSales_HeldSaleId",
                        column: x => x.HeldSaleId,
                        principalSchema: "sierra",
                        principalTable: "HeldSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportRows",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    RawJson = table.Column<string>(type: "text", nullable: false),
                    ValuesJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MessagesJson = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportRows_ImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "sierra",
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankStatements",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccountCode = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Opening = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Closing = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatements_LedgerAccounts_AccountCode",
                        column: x => x.AccountCode,
                        principalSchema: "sierra",
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerBudgets",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccountCode = table.Column<int>(type: "integer", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CostCentre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerBudgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerBudgets_LedgerAccounts_AccountCode",
                        column: x => x.AccountCode,
                        principalSchema: "sierra",
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TillShifts",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Cashier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccountCode = table.Column<int>(type: "integer", nullable: false),
                    Opened = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OpeningCount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    OpeningBook = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TillShifts_LedgerAccounts_AccountCode",
                        column: x => x.AccountCode,
                        principalSchema: "sierra",
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenancePlanEnds",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePlanEnds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenancePlanEnds_MaintenancePlans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "sierra",
                        principalTable: "MaintenancePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceJobs",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Customer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Site = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Serial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Equipment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Problem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReportedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanDue = table.Column<DateOnly>(type: "date", nullable: true),
                    ChecklistJson = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceJobs_MaintenancePlans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "sierra",
                        principalTable: "MaintenancePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentTerms",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CustomerWarrantyMonths = table.Column<int>(type: "integer", nullable: false),
                    StartsAt = table.Column<int>(type: "integer", nullable: false),
                    SupplierWarrantyMonths = table.Column<int>(type: "integer", nullable: false),
                    RequiresInstallation = table.Column<bool>(type: "boolean", nullable: false),
                    ChecklistJson = table.Column<string>(type: "text", nullable: false),
                    Coverage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentTerms_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "sierra",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PricePolicies",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinimumPrice = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricePolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricePolicies_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "sierra",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductBarcodes",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Conversion = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBarcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductBarcodes_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "sierra",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductLinks",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductLinks_Products_EquipmentProductId",
                        column: x => x.EquipmentProductId,
                        principalSchema: "sierra",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductLinks_Products_ItemProductId",
                        column: x => x.ItemProductId,
                        principalSchema: "sierra",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxAssignments",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxAssignments_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "sierra",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReminderDismissals",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReminderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderDismissals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReminderDismissals_Reminders_ReminderId",
                        column: x => x.ReminderId,
                        principalSchema: "sierra",
                        principalTable: "Reminders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Movements",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: "MAIN"),
                    Serial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Batch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movements_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "sierra",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_StockLocations_Location",
                        column: x => x.Location,
                        principalSchema: "sierra",
                        principalTable: "StockLocations",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoredDocumentLinks",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Target = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredDocumentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredDocumentLinks_StoredDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "StoredDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoredDocumentRevisions",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DetailsJson = table.Column<string>(type: "text", nullable: false),
                    Voided = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredDocumentRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredDocumentRevisions_StoredDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "StoredDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Pages = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<float>(type: "real", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredFiles_StoredDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "StoredDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenderBids",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    LinesJson = table.Column<string>(type: "text", nullable: false),
                    TaxRate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Tax = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CostTotal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderBids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenderBids_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalSchema: "sierra",
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenderEvents",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    BidVersion = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenderEvents_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalSchema: "sierra",
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderClosures",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderClosures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderClosures_TradeOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "sierra",
                        principalTable: "TradeOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderCurrencies",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderCurrencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderCurrencies_TradeOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "sierra",
                        principalTable: "TradeOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderFulfillments",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderFulfillments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderFulfillments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderFulfillments_TradeOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "sierra",
                        principalTable: "TradeOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockReservations",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Serial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Batch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockReservations_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "sierra",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservations_StockLocations_Location",
                        column: x => x.Location,
                        principalSchema: "sierra",
                        principalTable: "StockLocations",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservations_TradeOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "sierra",
                        principalTable: "TradeOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenderAwards",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    BidVersion = table.Column<int>(type: "integer", nullable: false),
                    LinesJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderAwards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenderAwards_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalSchema: "sierra",
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenderAwards_TradeOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "sierra",
                        principalTable: "TradeOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeOrderLines",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeOrderLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "sierra",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeOrderLines_TradeOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "sierra",
                        principalTable: "TradeOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VariantMembers",
                schema: "sierra",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValuesJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariantMembers", x => x.ProductId);
                    table.ForeignKey(
                        name: "FK_VariantMembers_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "sierra",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VariantMembers_VariantGroups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "sierra",
                        principalTable: "VariantGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerCreditUses",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCreditUses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerCreditUses_CustomerCredits_CreditId",
                        column: x => x.CreditId,
                        principalSchema: "sierra",
                        principalTable: "CustomerCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCreditUses_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InstallationCancellations",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationCancellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallationCancellations_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalSchema: "sierra",
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InstallationCompletions",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Completed = table.Column<DateOnly>(type: "date", nullable: false),
                    ChecklistDoneJson = table.Column<string>(type: "text", nullable: false),
                    AcceptedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TrainingGivenTo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallationCompletions_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalSchema: "sierra",
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InstallationUnits",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Serial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChecklistJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallationUnits_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalSchema: "sierra",
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IssuedChequeEvents",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChequeId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedChequeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssuedChequeEvents_IssuedCheques_ChequeId",
                        column: x => x.ChequeId,
                        principalSchema: "sierra",
                        principalTable: "IssuedCheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChequeEvents",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChequeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    PreviousState = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    BankAccount = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChequeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChequeEvents_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChequeEvents_ReceivedCheques_ChequeId",
                        column: x => x.ChequeId,
                        principalSchema: "sierra",
                        principalTable: "ReceivedCheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCreditUses",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCreditUses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierCreditUses_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditUses_SupplierCredits_CreditId",
                        column: x => x.CreditId,
                        principalSchema: "sierra",
                        principalTable: "SupplierCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollPaymentLines",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPaymentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollPaymentLines_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollPaymentLines_PayrollItems_PayrollItemId",
                        column: x => x.PayrollItemId,
                        principalSchema: "sierra",
                        principalTable: "PayrollItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementRows",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Line = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementRows_BankStatements_StatementId",
                        column: x => x.StatementId,
                        principalSchema: "sierra",
                        principalTable: "BankStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementVoids",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementVoids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementVoids_BankStatements_StatementId",
                        column: x => x.StatementId,
                        principalSchema: "sierra",
                        principalTable: "BankStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TillCloses",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Closed = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Expected = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Counted = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillCloses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TillCloses_TillShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalSchema: "sierra",
                        principalTable: "TillShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TillEntries",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TillEntries_Journal_JournalLineId",
                        column: x => x.JournalLineId,
                        principalSchema: "sierra",
                        principalTable: "Journal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TillEntries_TillShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalSchema: "sierra",
                        principalTable: "TillShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceJobEvents",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceJobEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceJobEvents_ServiceJobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "sierra",
                        principalTable: "ServiceJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductLinkRemovals",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductLinkRemovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductLinkRemovals_ProductLinks_LinkId",
                        column: x => x.LinkId,
                        principalSchema: "sierra",
                        principalTable: "ProductLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceMatches",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptMovementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    SourceAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    ExpectedSource = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    GoodsCleared = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    PriceVariance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    InventoryAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CostOfSalesAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceMatches_Documents_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceMatches_Documents_ReceiptId",
                        column: x => x.ReceiptId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceMatches_Movements_ReceiptMovementId",
                        column: x => x.ReceiptMovementId,
                        principalSchema: "sierra",
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LandedCostAllocations",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptMovementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Basis = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    InventoryAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CostOfSalesAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandedCostAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocations_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "sierra",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocations_Movements_ReceiptMovementId",
                        column: x => x.ReceiptMovementId,
                        principalSchema: "sierra",
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoredDocumentLinkRemovals",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredDocumentLinkRemovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredDocumentLinkRemovals_StoredDocumentLinks_LinkId",
                        column: x => x.LinkId,
                        principalSchema: "sierra",
                        principalTable: "StoredDocumentLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReservationReleases",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReservationReleases_StockReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalSchema: "sierra",
                        principalTable: "StockReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankMatches",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StatementRowId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClearedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StatementReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankMatches_BankStatementRows_StatementRowId",
                        column: x => x.StatementRowId,
                        principalSchema: "sierra",
                        principalTable: "BankStatementRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankMatches_Journal_JournalLineId",
                        column: x => x.JournalLineId,
                        principalSchema: "sierra",
                        principalTable: "Journal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankMatchCancellations",
                schema: "sierra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankMatchCancellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankMatchCancellations_BankMatches_MatchId",
                        column: x => x.MatchId,
                        principalSchema: "sierra",
                        principalTable: "BankMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "sierra",
                table: "LedgerAccounts",
                columns: new[] { "Id", "CashOrBank", "Control", "Group", "Name" },
                values: new object[,]
                {
                    { 1000, true, false, 0, "Cash" },
                    { 1010, true, false, 0, "Bank" },
                    { 1020, false, true, 0, "Cheques in hand" },
                    { 1030, false, true, 0, "Cheques deposited awaiting clearance" },
                    { 1100, false, true, 0, "Trade receivables" },
                    { 1200, false, true, 0, "Inventory" },
                    { 1250, false, true, 0, "Delivered goods awaiting invoice" },
                    { 1260, false, true, 0, "Service jobs in progress" },
                    { 1300, false, true, 0, "Supplier refunds and credits" },
                    { 1310, false, true, 0, "Employee loans and advances" },
                    { 2000, false, true, 1, "Trade payables" },
                    { 2050, false, true, 1, "Goods received not invoiced" },
                    { 2100, false, false, 1, "Tax payable" },
                    { 2200, false, false, 1, "Payroll payable" },
                    { 2300, false, false, 1, "Payroll deductions payable" },
                    { 2310, false, true, 1, "PAYE payable" },
                    { 2320, false, true, 1, "NASSIT payable" },
                    { 2400, false, true, 1, "Customer credits" },
                    { 3000, false, false, 2, "Owner capital" },
                    { 3100, false, false, 2, "Retained earnings" },
                    { 3200, false, true, 2, "Opening balance equity" },
                    { 4000, false, true, 3, "Sales" },
                    { 4100, false, false, 3, "Other income" },
                    { 4200, false, true, 3, "Exchange gains" },
                    { 4300, false, true, 3, "Service income" },
                    { 5000, false, true, 4, "Cost of sales" },
                    { 5100, false, false, 4, "Operating expenses" },
                    { 5200, false, false, 4, "Wages and salaries" },
                    { 5210, false, true, 4, "Employer NASSIT contributions" },
                    { 5300, false, true, 4, "Exchange losses" },
                    { 5400, false, true, 4, "Warranty and service costs" }
                });

            migrationBuilder.InsertData(
                schema: "sierra",
                table: "StockLocations",
                columns: new[] { "Code", "Name" },
                values: new object[] { "MAIN", "Main warehouse" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_RequestId",
                schema: "sierra",
                table: "ApprovalDecisions",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalNotes_RequestId",
                schema: "sierra",
                table: "ApprovalNotes",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Branch_PayloadHash",
                schema: "sierra",
                table: "ApprovalRequests",
                columns: new[] { "Branch", "PayloadHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Number",
                schema: "sierra",
                table: "ApprovalRequests",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalUses_OperationId",
                schema: "sierra",
                table: "ApprovalUses",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalUses_RequestId",
                schema: "sierra",
                table: "ApprovalUses",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "sierra",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "sierra",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "sierra",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "sierra",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "sierra",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "sierra",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "sierra",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankFormats_Bank",
                schema: "sierra",
                table: "BankFormats",
                column: "Bank",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMatchCancellations_MatchId",
                schema: "sierra",
                table: "BankMatchCancellations",
                column: "MatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMatches_JournalLineId",
                schema: "sierra",
                table: "BankMatches",
                column: "JournalLineId");

            migrationBuilder.CreateIndex(
                name: "IX_BankMatches_StatementRowId",
                schema: "sierra",
                table: "BankMatches",
                column: "StatementRowId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementRows_StatementId_Line",
                schema: "sierra",
                table: "BankStatementRows",
                columns: new[] { "StatementId", "Line" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_AccountCode",
                schema: "sierra",
                table: "BankStatements",
                column: "AccountCode");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_Branch_AccountCode_ContentHash",
                schema: "sierra",
                table: "BankStatements",
                columns: new[] { "Branch", "AccountCode", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_Branch_AccountCode_Reference",
                schema: "sierra",
                table: "BankStatements",
                columns: new[] { "Branch", "AccountCode", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementVoids_StatementId",
                schema: "sierra",
                table: "BankStatementVoids",
                column: "StatementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillAllocations_InvoiceId",
                schema: "sierra",
                table: "BillAllocations",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_BillAllocations_VoucherId_InvoiceId",
                schema: "sierra",
                table: "BillAllocations",
                columns: new[] { "VoucherId", "InvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bundles_Code",
                schema: "sierra",
                table: "Bundles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaptureRevisions_CaptureId_Version",
                schema: "sierra",
                table: "CaptureRevisions",
                columns: new[] { "CaptureId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Captures_Branch_Sha256",
                schema: "sierra",
                table: "Captures",
                columns: new[] { "Branch", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChequeEvents_ChequeId_Version",
                schema: "sierra",
                table: "ChequeEvents",
                columns: new[] { "ChequeId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChequeEvents_DocumentId",
                schema: "sierra",
                table: "ChequeEvents",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfiles_Branch_Version",
                schema: "sierra",
                table: "CompanyProfiles",
                columns: new[] { "Branch", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ControlPolicies_Branch_Version",
                schema: "sierra",
                table: "ControlPolicies",
                columns: new[] { "Branch", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCredits_ReturnId",
                schema: "sierra",
                table: "CustomerCredits",
                column: "ReturnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditUses_CreditId",
                schema: "sierra",
                table: "CustomerCreditUses",
                column: "CreditId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditUses_DocumentId",
                schema: "sierra",
                table: "CustomerCreditUses",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInvoices_DeliveryId",
                schema: "sierra",
                table: "DeliveryInvoices",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInvoices_InvoiceId",
                schema: "sierra",
                table: "DeliveryInvoices",
                column: "InvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Number",
                schema: "sierra",
                table: "Documents",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Reverses",
                schema: "sierra",
                table: "Documents",
                column: "Reverses",
                unique: true,
                filter: "\"Reverses\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplates_Branch_Version",
                schema: "sierra",
                table: "DocumentTemplates",
                columns: new[] { "Branch", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentThumbnails_Sha256",
                schema: "sierra",
                table: "DocumentThumbnails",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLoans_DocumentId",
                schema: "sierra",
                table: "EmployeeLoans",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLoans_EmployeeId",
                schema: "sierra",
                table: "EmployeeLoans",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Branch_Code",
                schema: "sierra",
                table: "Employees",
                columns: new[] { "Branch", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentTerms_ProductId_Version",
                schema: "sierra",
                table: "EquipmentTerms",
                columns: new[] { "ProductId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_Branch_Currency_EffectiveFrom",
                schema: "sierra",
                table: "ExchangeRates",
                columns: new[] { "Branch", "Currency", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileBlobs_Sha256",
                schema: "sierra",
                table: "FileBlobs",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalSubmissions_Branch_Outcome",
                schema: "sierra",
                table: "FiscalSubmissions",
                columns: new[] { "Branch", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalSubmissions_DocumentId",
                schema: "sierra",
                table: "FiscalSubmissions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ForeignBills_DocumentId",
                schema: "sierra",
                table: "ForeignBills",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForeignSettlements_BillId",
                schema: "sierra",
                table: "ForeignSettlements",
                column: "BillId");

            migrationBuilder.CreateIndex(
                name: "IX_ForeignSettlements_VoucherId",
                schema: "sierra",
                table: "ForeignSettlements",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_HeldSaleEnds_DocumentId",
                schema: "sierra",
                table: "HeldSaleEnds",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_HeldSaleEnds_HeldSaleId",
                schema: "sierra",
                table: "HeldSaleEnds",
                column: "HeldSaleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_Branch_Key_Status",
                schema: "sierra",
                table: "ImportBatches",
                columns: new[] { "Branch", "Key", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_BatchId_RowNumber",
                schema: "sierra",
                table: "ImportRows",
                columns: new[] { "BatchId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallationCancellations_InstallationId",
                schema: "sierra",
                table: "InstallationCancellations",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallationCompletions_InstallationId",
                schema: "sierra",
                table: "InstallationCompletions",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Installations_Number",
                schema: "sierra",
                table: "Installations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Installations_SaleId",
                schema: "sierra",
                table: "Installations",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_InstallationUnits_InstallationId_ProductId_Serial",
                schema: "sierra",
                table: "InstallationUnits",
                columns: new[] { "InstallationId", "ProductId", "Serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallationUnits_Serial",
                schema: "sierra",
                table: "InstallationUnits",
                column: "Serial");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRevisions_Branch_Kind_Key_Version",
                schema: "sierra",
                table: "InventoryRevisions",
                columns: new[] { "Branch", "Kind", "Key", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceMatches_InvoiceId_ReceiptMovementId",
                schema: "sierra",
                table: "InvoiceMatches",
                columns: new[] { "InvoiceId", "ReceiptMovementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceMatches_ReceiptId",
                schema: "sierra",
                table: "InvoiceMatches",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceMatches_ReceiptMovementId",
                schema: "sierra",
                table: "InvoiceMatches",
                column: "ReceiptMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedChequeEvents_ChequeId",
                schema: "sierra",
                table: "IssuedChequeEvents",
                column: "ChequeId");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCheques_Branch_BankAccount_Number",
                schema: "sierra",
                table: "IssuedCheques",
                columns: new[] { "Branch", "BankAccount", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCheques_VoucherId",
                schema: "sierra",
                table: "IssuedCheques",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_JobRuns_Branch_Job",
                schema: "sierra",
                table: "JobRuns",
                columns: new[] { "Branch", "Job" });

            migrationBuilder.CreateIndex(
                name: "IX_Journal_DocumentId",
                schema: "sierra",
                table: "Journal",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_DocumentId",
                schema: "sierra",
                table: "LandedCostAllocations",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_ReceiptMovementId",
                schema: "sierra",
                table: "LandedCostAllocations",
                column: "ReceiptMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerBudgets_AccountCode",
                schema: "sierra",
                table: "LedgerBudgets",
                column: "AccountCode");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlanEnds_PlanId",
                schema: "sierra",
                table: "MaintenancePlanEnds",
                column: "PlanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_Number",
                schema: "sierra",
                table: "MaintenancePlans",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Movements_Branch_ProductId",
                schema: "sierra",
                table: "Movements",
                columns: new[] { "Branch", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_DocumentId",
                schema: "sierra",
                table: "Movements",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_Location",
                schema: "sierra",
                table: "Movements",
                column: "Location");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_ProductId",
                schema: "sierra",
                table: "Movements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderClosures_OrderId",
                schema: "sierra",
                table: "OrderClosures",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderCurrencies_OrderId",
                schema: "sierra",
                table: "OrderCurrencies",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderFulfillments_DocumentId",
                schema: "sierra",
                table: "OrderFulfillments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderFulfillments_OrderId",
                schema: "sierra",
                table: "OrderFulfillments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Parties_Branch_Code",
                schema: "sierra",
                table: "Parties",
                columns: new[] { "Branch", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parties_Branch_NormalizedName",
                schema: "sierra",
                table: "Parties",
                columns: new[] { "Branch", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollItems_DocumentId",
                schema: "sierra",
                table: "PayrollItems",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollItems_EmployeeId",
                schema: "sierra",
                table: "PayrollItems",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPaymentLines_DocumentId",
                schema: "sierra",
                table: "PayrollPaymentLines",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPaymentLines_PayrollItemId",
                schema: "sierra",
                table: "PayrollPaymentLines",
                column: "PayrollItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRules_Branch_EffectiveFrom",
                schema: "sierra",
                table: "PayrollRules",
                columns: new[] { "Branch", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRules_Branch_Version",
                schema: "sierra",
                table: "PayrollRules",
                columns: new[] { "Branch", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PricePolicies_ProductId_Version",
                schema: "sierra",
                table: "PricePolicies",
                columns: new[] { "ProductId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceRules_Code",
                schema: "sierra",
                table: "PriceRules",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodes_Code",
                schema: "sierra",
                table: "ProductBarcodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodes_ProductId",
                schema: "sierra",
                table: "ProductBarcodes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductLinkRemovals_LinkId",
                schema: "sierra",
                table: "ProductLinkRemovals",
                column: "LinkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductLinks_EquipmentProductId",
                schema: "sierra",
                table: "ProductLinks",
                column: "EquipmentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductLinks_ItemProductId",
                schema: "sierra",
                table: "ProductLinks",
                column: "ItemProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku",
                schema: "sierra",
                table: "Products",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedCheques_Branch_Party_Reference_Bank",
                schema: "sierra",
                table: "ReceivedCheques",
                columns: new[] { "Branch", "Party", "Reference", "Bank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedCheques_SourceDocumentId",
                schema: "sierra",
                table: "ReceivedCheques",
                column: "SourceDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReminderDismissals_ReminderId_Actor",
                schema: "sierra",
                table: "ReminderDismissals",
                columns: new[] { "ReminderId", "Actor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_Branch_Key",
                schema: "sierra",
                table: "Reminders",
                columns: new[] { "Branch", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReservationReleases_ReservationId",
                schema: "sierra",
                table: "ReservationReleases",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAllocations_OriginalDocumentId",
                schema: "sierra",
                table: "ReturnAllocations",
                column: "OriginalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAllocations_ReturnDocumentId",
                schema: "sierra",
                table: "ReturnAllocations",
                column: "ReturnDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxRates_Branch_From",
                schema: "sierra",
                table: "SalesTaxRates",
                columns: new[] { "Branch", "From" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobEvents_DocumentId",
                schema: "sierra",
                table: "ServiceJobEvents",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobEvents_JobId_Sequence",
                schema: "sierra",
                table: "ServiceJobEvents",
                columns: new[] { "JobId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_Number",
                schema: "sierra",
                table: "ServiceJobs",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_PlanId_PlanDue",
                schema: "sierra",
                table: "ServiceJobs",
                columns: new[] { "PlanId", "PlanDue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_ProductId_Serial",
                schema: "sierra",
                table: "ServiceJobs",
                columns: new[] { "ProductId", "Serial" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryRemittances_Branch_Month_Kind",
                schema: "sierra",
                table: "StatutoryRemittances",
                columns: new[] { "Branch", "Month", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryRemittances_DocumentId",
                schema: "sierra",
                table: "StatutoryRemittances",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_Location",
                schema: "sierra",
                table: "StockReservations",
                column: "Location");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderId",
                schema: "sierra",
                table: "StockReservations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_ProductId",
                schema: "sierra",
                table: "StockReservations",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredDocumentLinkRemovals_LinkId",
                schema: "sierra",
                table: "StoredDocumentLinkRemovals",
                column: "LinkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredDocumentLinks_DocumentId",
                schema: "sierra",
                table: "StoredDocumentLinks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredDocumentLinks_Target_Key",
                schema: "sierra",
                table: "StoredDocumentLinks",
                columns: new[] { "Target", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredDocumentRevisions_DocumentId_Version",
                schema: "sierra",
                table: "StoredDocumentRevisions",
                columns: new[] { "DocumentId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredDocuments_Branch",
                schema: "sierra",
                table: "StoredDocuments",
                column: "Branch");

            migrationBuilder.CreateIndex(
                name: "IX_StoredDocuments_Number",
                schema: "sierra",
                table: "StoredDocuments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_DocumentId_Version",
                schema: "sierra",
                table: "StoredFiles",
                columns: new[] { "DocumentId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_Sha256",
                schema: "sierra",
                table: "StoredFiles",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCredits_ReturnId",
                schema: "sierra",
                table: "SupplierCredits",
                column: "ReturnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditUses_CreditId",
                schema: "sierra",
                table: "SupplierCreditUses",
                column: "CreditId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditUses_DocumentId",
                schema: "sierra",
                table: "SupplierCreditUses",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_Commit",
                schema: "sierra",
                table: "SyncChanges",
                column: "Commit");

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_Origin_Seq",
                schema: "sierra",
                table: "SyncChanges",
                columns: new[] { "Origin", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_Scope_Seq",
                schema: "sierra",
                table: "SyncChanges",
                columns: new[] { "Scope", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_Time",
                schema: "sierra",
                table: "SyncConflicts",
                column: "Time");

            migrationBuilder.CreateIndex(
                name: "IX_TaxAssignments_ProductId_From",
                schema: "sierra",
                table: "TaxAssignments",
                columns: new[] { "ProductId", "From" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenderAwards_OrderId",
                schema: "sierra",
                table: "TenderAwards",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TenderAwards_TenderId",
                schema: "sierra",
                table: "TenderAwards",
                column: "TenderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenderBids_TenderId_Version",
                schema: "sierra",
                table: "TenderBids",
                columns: new[] { "TenderId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenderEvents_TenderId_Sequence",
                schema: "sierra",
                table: "TenderEvents",
                columns: new[] { "TenderId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_Number",
                schema: "sierra",
                table: "Tenders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TillAssignments_Branch_AccountCode_Cashier",
                schema: "sierra",
                table: "TillAssignments",
                columns: new[] { "Branch", "AccountCode", "Cashier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TillCloses_ShiftId",
                schema: "sierra",
                table: "TillCloses",
                column: "ShiftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TillEntries_JournalLineId",
                schema: "sierra",
                table: "TillEntries",
                column: "JournalLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TillEntries_ShiftId",
                schema: "sierra",
                table: "TillEntries",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_TillShifts_AccountCode",
                schema: "sierra",
                table: "TillShifts",
                column: "AccountCode");

            migrationBuilder.CreateIndex(
                name: "IX_TradeOrderLines_OrderId_ProductId",
                schema: "sierra",
                table: "TradeOrderLines",
                columns: new[] { "OrderId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeOrderLines_ProductId",
                schema: "sierra",
                table: "TradeOrderLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeOrders_Number",
                schema: "sierra",
                table: "TradeOrders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeOrders_QuotationId",
                schema: "sierra",
                table: "TradeOrders",
                column: "QuotationId",
                unique: true,
                filter: "\"QuotationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UnitProfiles_ProductId_Serial_Version",
                schema: "sierra",
                table: "UnitProfiles",
                columns: new[] { "ProductId", "Serial", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VariantGroups_Code",
                schema: "sierra",
                table: "VariantGroups",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VariantMembers_GroupId",
                schema: "sierra",
                table: "VariantMembers",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Warranties_SaleId_ProductId_Serial",
                schema: "sierra",
                table: "Warranties",
                columns: new[] { "SaleId", "ProductId", "Serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warranties_Serial",
                schema: "sierra",
                table: "Warranties",
                column: "Serial");

            migrationBuilder.CreateIndex(
                name: "IX_YearEndClosures_Branch_YearEnd",
                schema: "sierra",
                table: "YearEndClosures",
                columns: new[] { "Branch", "YearEnd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalDecisions",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ApprovalNotes",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ApprovalUses",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "BankFormats",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "BankMatchCancellations",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "BankStatementVoids",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "BillAllocations",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Bundles",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "CaptureRevisions",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ChequeEvents",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "CompanyProfiles",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ControlPolicies",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "CustomerCreditUses",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "DeliveryInvoices",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "DocumentTemplates",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "DocumentThumbnails",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "EmployeeLoans",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "EquipmentTerms",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ExchangeRates",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "FileBlobs",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "FiscalSubmissions",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ForeignBills",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ForeignSettlements",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "HeldSaleEnds",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ImportRows",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "InstallationCancellations",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "InstallationCompletions",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "InstallationUnits",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "IntegritySeals",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "InventoryRevisions",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "InvoiceMatches",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "IssuedChequeEvents",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "JobRuns",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "JobStates",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "LandedCostAllocations",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "LedgerBudgets",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "MaintenancePlanEnds",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Operations",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "OrderClosures",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "OrderCurrencies",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "OrderFulfillments",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Parties",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "PayrollPaymentLines",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "PayrollRules",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "PeriodClosures",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "PricePolicies",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "PriceRules",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ProductBarcodes",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ProductLinkRemovals",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ReminderDismissals",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ReservationReleases",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ReturnAllocations",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "SalesTaxRates",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ServiceJobEvents",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "StatutoryRemittances",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "StoredDocumentLinkRemovals",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "StoredDocumentRevisions",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "StoredFiles",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "SupplierCreditUses",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "SyncChanges",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "SyncConflicts",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "SyncPeers",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "TaxAssignments",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "TenderAwards",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "TenderBids",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "TenderEvents",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "TillAssignments",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "TillCloses",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "TillEntries",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "TradeOrderLines",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "UnitProfiles",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "UpdateStatuses",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "VariantMembers",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Warranties",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "YearEndClosures",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ApprovalRequests",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "BankMatches",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Captures",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ReceivedCheques",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "CustomerCredits",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "HeldSales",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ImportBatches",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Installations",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "IssuedCheques",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Movements",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "PayrollItems",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ProductLinks",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Reminders",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "StockReservations",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "ServiceJobs",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "StoredDocumentLinks",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "SupplierCredits",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Tenders",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "TillShifts",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "VariantGroups",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "BankStatementRows",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Journal",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Employees",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Products",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "StockLocations",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "TradeOrders",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "MaintenancePlans",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "StoredDocuments",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "BankStatements",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "Documents",
                schema: "sierra");

            migrationBuilder.DropTable(
                name: "LedgerAccounts",
                schema: "sierra");
        }
    }
}
