# Raikou Manip web

English expert-facing strategy reader adapted from the native Raikou Manip app: dense route rows, four band buttons, no setup instructions or TF confirmations, no arrival row, static enlarged emotes, original starts only once the finish is determined.

`src/styles.css` owns the browser adaptation of the native Theme: primary #18585b, background #eff3f3, surface #ffffff, text #203537, muted #53696b, border #c4d2d2, accent #9b451d, accent background #fff1e6, danger #a32135. Segoe UI body 16, Trebuchet MS headings, Consolas turn arrows 19. System fonts, no downloads. Browser forced-colors uses system roles. No animation or decorative content.

The native StrategyEngine owns policy projection and observable response grouping. ExportWeb generates all states using that implementation and independently walks Choose/Back to compare them. ReaderState owns browser navigation, revisions, and structural validation; app.mjs owns DOM rendering and asynchronous loading. No game logic is reimplemented in the browser. The source exports remain unchanged.

One-column panels, desktop max-width 1280 with 12px outer inset. Identification actions may scroll within a short upper panel; response lists use normal document scrolling. Finishing actions expand. At 580px and below, toolbar wraps into its own full-width row, bands form a 2×2 grid, and choice rows have at least 48px height. Text wraps; no clipped dialogue or horizontal page scroll. Native emote PNG frames retain their bytes; generated CSS crops the transparent canvas and displays visible artwork at 1.5×, centered inline with a 6px gap. Emote labels are accessibility text only.

Real buttons and search input, visible focus, default/hover/pressed/disabled states, accessible region headings, details aria-expanded, polite status/remaining-count announcements. Shortcuts 1–9, Alt+Left, Ctrl/Cmd+F follow the native workflow and do not consume typed filter digits. Double-click second events and repeated keydowns do not select an extra branch. Focus returns to the route heading after a choice and to the selected band when reopening selection.

Loading is atomic and latest-request-wins. Selecting a different band aborts the old request; generation checks prevent stale results from replacing it. Failures stay on band selection with a terse message and Retry. No alerts, confirmation dialogs, browser persistence, cookies, analytics, remote assets, or timed progression. No unsupported live game-state claim: the visible finish is a complete future action route, and its original starts include every compatible candidate.

The static file boundary is `public/`; developer sources, source checkpoint context, and audit traces are not shipped as site assets. Content uses text nodes rather than HTML interpolation. All relative URLs support both domain-root and subpath hosting. Caddy deployment stays user-controlled.

Verification: compare every screen to native projection, traverse every browser state, and inspect real browser desktop/mobile captures. UI checks include centered 24px standard emotes, natural Left separated from TF count, explicit BONK, and original starting-advance lists. Root and `/raikou/` paths, keyboard/filter, Back/Restart, network error/retry, and late response handling are covered.
