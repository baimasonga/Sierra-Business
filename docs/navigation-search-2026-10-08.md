# Screen navigation search

The shared web and Windows workspace now has a Go to a screen search above its grouped menu. Results match screen names and business areas, ignore case, and require every search term to match. Only screens available to the signed-in user's permissions appear.

Select a result or press Enter to open the first result. Escape clears the query. Alt+G opens the compact menu when necessary and focuses search. Navigation retains the existing unfinished-document confirmation and sale recovery checks.

Verification: navigation search tests cover area matching, multiple terms, case, empty queries and restricted screens. Rendered workspace checks cover navigation across business areas and no-match feedback. Native Windows keyboard and visual acceptance remain pending. This source change is included in the verified 0.29.1 installer; it is not present in the older 0.29.0 installer.
