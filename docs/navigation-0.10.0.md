# Grouped workspace navigation — 0.10.0

The long flat menu is replaced by seven business areas. Choose an area in the sidebar, then choose its screen from the horizontal navigation above the workspace. Access restrictions continue to filter screens and omit empty groups.

| Area | Screens |
|---|---|
| Overview | Business overview |
| Sales & purchases | New sale, Receive stock, Orders & quotations, Customers & suppliers, Returns, Sales controls |
| Inventory | Stock summary, Catalogue, Barcodes, Warehouse controls |
| Finance | Accounting, Banking, Tills, Trial balance, Periods & tax |
| People | Payroll |
| Documents | Document register; Scan & capture in the desktop host |
| Administration | Users & permissions, Audit & sync |

The company selector stays above the groups. Backup (when available and authorized) and sign-out remain below them. The desktop sidebar stays within the window and its navigation can scroll independently on unusually short screens. At 1366×768 all seven groups and sign-out fit without sidebar scrolling. On screens up to 800 pixels wide, Menu opens the grouped navigation and utility actions; choosing another area closes it. Screen links wrap rather than requiring a long horizontal scroll. Breadcrumbs and active highlights identify the current area and screen. Keyboard focus styling and a skip-to-workspace link are included. Existing unfinished-draft confirmation still applies when switching screens or business areas.

The browser host also now loads its build-time static web assets when launched directly: this fixes a missing Blazor script that left the rendered screen noninteractive. The HTTP verification script checks that script is served.

Browser validation covered opening the mobile menu, choosing Inventory, displaying its four screen links, opening Finance/Accounting, and desktop sidebar fit with no page-level horizontal overflow. Desktop and 390-pixel mobile screenshots were inspected. Native MAUI uses the same Razor component and stylesheet; full native and hardware acceptance remains outstanding. No database migration is required.
