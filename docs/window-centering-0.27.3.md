# Startup window and sign-in layout

7 October 2026: the Windows startup window is sized to the nearest monitor's
working area and centered there, leaving a 24 DIP margin. The calculation uses
the native window's DPI and the working area's coordinates, including monitor
offsets and taskbar space. Minimum dimensions are reduced when necessary to fit.
Positioning happens once at creation, so normal moving and resizing remain free.

The login panel uses flexible auto margins and tighter spacing. Change company
stays inline rather than taking a full-width button row. Short windows can scroll
from the top instead of clipping the start of the panel.

The Windows Release build passed with zero warnings and errors. A browser preview
using the production stylesheet at 800 x 600 showed a 430-pixel panel centered
at x=185, with equal top/bottom margins and the sign-in button visible. At
800 x 480 the panel started at y=16 and remained scrollable. This preview checks
the layout, not native Windows positioning; native launch verification remains
outstanding because native computer automation is unavailable in this session.

Installer 0.27.3 compiled successfully at
`artifacts/windows/0.27.3/SierraBusiness-Setup-x64.exe` (285,040,732 bytes).
The packaging suite passed 289 tests with zero failures and six provider skips;
offline OCR, native publish and Inno compilation passed. Packaged CSS matches
the source checksum. Installer SHA-256:
`96E95F50B111F2E64DE76584E190C8AF96E600460C039CC60D4A0E60BEA49EAD`.
The installer is unsigned and was not installed during this run.
