# WindBoard → Web: decision-grade comparison (2026-10-31)

## 1) WinUI-3-like UI library for the web

**Fluent UI React v9 is Microsoft's current web design system.** The fluentui monorepo ships three lines — React v9 ("new, future-proof", used by Microsoft 365), React v8 (Office), Web Components (Edge); Northstar is EOL since July 2025 ([repo README](https://github.com/microsoft/fluentui)). Latest stable `@fluentui/react-components` is **9.75.0, published 2026-10-27**, MIT ([registry](https://registry.npmjs.org/@fluentui/react-components)); docs live at [react.fluentui.dev](https://react.fluentui.dev/). It implements **Fluent 2**, "the next evolution of Microsoft's design system" ([fluent2.microsoft.design](https://fluent2.microsoft.design/)), and WinUI 3 apps are specified against the same Fluent Design guidance ([learn: Design Windows apps](https://learn.microsoft.com/en-us/windows/apps/design/)).

**Fidelity.** Tokens are shared in spirit, not values-identical: the web radius ramp is 2/4/6/8/12/16/24 px ([borderRadius.ts](https://github.com/microsoft/fluentui/blob/master/packages/tokens/src/global/borderRadius.ts)). WinUI's `ControlCornerRadius`/`OverlayCornerRadius` numbers could **not** be verified from a primary source (the Learn corner-radius page 404s; the XAML themeresources path is gone) — treat 4/8 px as folklore.

Control coverage maps cleanly, proven by the package's own dependencies: NavigationView → `@fluentui/react-nav` 9.4.9, ContentDialog → `react-dialog` 9.18.6, InfoBar → `react-message-bar` 9.7.9, TeachingTip → `react-teaching-popover` 9.7.9, Flyout → `react-popover`/`react-menu`, CommandBar → `react-toolbar`. InfoButton/Alert are still beta (9.0.1-beta.1 / 9.0.2-beta.1).

**Materials are the real gap.** Acrylic is blur + exclusion blend + tint + noise, auto-disabled in Battery Saver / High Contrast ([learn: Acrylic](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic)). The web substitute is `backdrop-filter` (Chrome 76, Firefox 103, Safari 18 unprefixed / 9 prefixed, per [BCD](https://github.com/mdn/browser-compat-data/blob/main/css/properties/backdrop-filter.json)) plus a tint and an SVG noise overlay — close, never identical. **Mica samples the desktop wallpaper once** ([learn: Mica](https://learn.microsoft.com/en-us/windows/apps/design/style/mica)); a browser tab has no wallpaper access, so Mica is *not reproducible* — use an opaque gradient.

Custom title bar: WinUI 3 has `AppWindow.TitleBar` ([learn](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar)); the web equivalent is PWA `window-controls-overlay` (**Chrome 105+, Firefox/Safari no**, [BCD](https://github.com/mdn/browser-compat-data/blob/main/manifests/webapp/display_override.json)).

**Alternatives.** `@fluentui/web-components` is alive — **v3.1.3 released 2026-08-25** on `@microsoft/fast-element ^3` ([registry](https://registry.npmjs.org/@fluentui/web-components)) — but Microsoft's own overview page is still dated 2021-11-01 ([learn](https://learn.microsoft.com/en-us/fluent-ui/web-components/)), signalling second-tier investment. **Uno Platform** genuinely runs WinUI/UWP XAML on WebAssembly with AOT/PG-AOT/interpreted modes ([platform.uno](https://platform.uno/uno-platform-for-web-webassembly/)), but publishes **no payload figure** (unverified; .NET-WASM apps are typically multi-MB before your SkiaSharp ink layer). **Avalonia WASM** is officially supported with `RenderingMode = [WebGL2, WebGL1, Software2D]` fallback and `PreferFileDialogPolyfill` ([docs](https://docs.avaloniaui.net/docs/platform-specific-guides/webassembly)) — also size-unpublished, and its Fluent theme is not pixel-identical to WinUI 3. No maintained "WinUI clone" React kit exists.

**Recommendation:** Fluent UI React v9 + a thin CSS layer for Acrylic-ish surfaces and an opaque Mica stand-in. Do not ship Uno/Avalonia WASM for a drawing-first app in 2026 — you inherit a .NET toolchain and multi-MB startup to still miss Mica.
**Confidence: high** on library facts, **medium** on the visual-fidelity judgement.

## 2) Ink rendering

Use **`perfect-freehand`** (MIT, 1.2.3 / 2026-02-01, [repo](https://github.com/steveruizok/perfect-freehand)) to turn pressure samples into a filled outline polygon; Excalidraw pins 1.2.0 for exactly this ([package.json](https://github.com/excalidraw/excalidraw/blob/master/packages/excalidraw/package.json)). Write custom only if you beat it: tldraw's 5.2 rewrite derives direction from neighbouring points and computes radius+taper in one outline pass — "about 2x faster … 57% smaller SVG path data" ([tldraw blog](https://tldraw.dev/blog/tldraw-sdk-5.2)).

**Renderer:** Canvas2D is right for thousands of strokes; keep committed strokes in a backing bitmap and repaint only the active stroke. Move to OffscreenCanvas + worker only when you must (`transferControlToOffscreen`: Chrome 69 / Firefox 105 / Safari 16.4, [MDN](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/canvas)). WebGL/WebGPU buys you textured brushes, not baseline correctness.

**Events:** prefer `pointerrawupdate` (Chrome 77+, Firefox 148, **Safari no**) and fall back to `getCoalescedEvents()` inside `pointermove` (Chrome 58 / Firefox 59 / **Safari 18.2**, marked "limited availability" — [MDN](https://developer.mozilla.org/en-US/docs/Web/API/PointerEvent/getCoalescedEvents)). `pressure` is available everywhere (Chrome 55 / Firefox 59 / Safari 13) but a mouse reports 0.5 while down and touch often reports 0/1 — normalize, and treat Apple-Pencil pressure on iOS as *plausible but unverified here*.

**Limits:** iOS caps canvases at **4096×4096** ([MDN](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/canvas#maximum_canvas_size)) — so an "infinite canvas" must be a viewport-sized canvas plus a transform, never a giant bitmap. Scale the backing store by `devicePixelRatio` (cap ~2). Set `touch-action: none` on the canvas so the browser stops panning/pinching, and expect `pointercancel` when it takes over anyway ([MDN touch-action](https://developer.mozilla.org/en-US/docs/Web/CSS/touch-action)). Ignore `pointerType === 'touch'` while a pen is down (palm rejection).

**Prior art:** Excalidraw (perfect-freehand + roughjs), tldraw (custom ink engine), Microsoft Whiteboard web — no published engineering note found (unverified).

**Recommendation:** perfect-freehand + Canvas2D on a DPR-scaled viewport canvas, `pointerrawupdate` with coalesced-events fallback, OffscreenCanvas deferred.
**Confidence: high** on mechanics, **medium** on perf ceilings.

## 3) File IO

**ZIP:** `fflate` (MIT, 0.8.3, "8kB package" + 3kB for ZIP, streaming, 4 GB files, vendor claim of up to 50% faster than pako — [repo](https://github.com/101arrowz/fflate)) is the default for 10–50 MB `.wbix`. Use `@zip.js/zip.js` (BSD-3, 2.24.0 / 2026-10-16) if you need Zip64 >4 GB, encryption, incremental writing or worker plumbing ([repo](https://github.com/gildas-lormeau/zip.js)). Avoid JSZip for this size: `generateAsync` "hold[s] the full result in memory", ZIP64 is 32-bit-constrained, and metadata is dropped on rewrite ([limitations](https://stuk.github.io/jszip/documentation/limitations.html)).

**PNG:** `canvas.toBlob('image/png')` — PNG is mandatory in all browsers, `quality` only affects lossy types, and resolution metadata is written at 96 dpi only "for file formats that support" it ([MDN](https://developer.mozilla.org/en-US/docs/Web/API/HTMLCanvasElement/toBlob)). To hit 300 dpi, render at `300/96 ×` logical size; transparency survives if you clear rather than fill.

**PDF:** `pdf-lib` 1.17.1 (published 2021-11-06, last repo push 2024-07-17, [repo](https://github.com/Hopding/pdf-lib)) is effectively unmaintained but stable and ideal for "one PNG per page at exact point size". `jsPDF` 4.2.1 (2026-03-17) is actively maintained ([repo](https://github.com/parallax/jsPDF)); `pdfkit` is Node-stream-oriented. Since WindBoard pages are rasterised ink, vector text fidelity is irrelevant — embed the raster.

**File System Access API:** `showSaveFilePicker`/`showOpenFilePicker`/`showDirectoryPicker` are **Chromium 86+ only** (Firefox false, Safari false, [BCD](https://github.com/mdn/browser-compat-data/blob/main/api/Window.json)); they need transient user activation and throw `AbortError` on dismiss ([MDN](https://developer.mozilla.org/en-US/docs/Web/API/Window/showSaveFilePicker)). Fall back to `<a download>`, `<input type=file>`, and drag-drop.

**Persistence:** OPFS is Baseline since March 2023, worker-only sync access handles ([MDN](https://developer.mozilla.org/en-US/docs/Web/API/File_System_API/Origin_private_file_system)); use OPFS for page blobs/autosave and IndexedDB for metadata and the undo journal.

**Recommendation:** fflate + `toBlob` + pdf-lib + FSA-with-download-fallback + OPFS autosave, all behind one `PlatformIO` interface that mirrors your existing WBIX reader/writer.
**Confidence: high.**

## 4) Deployment / shell

Installability needs HTTPS, a manifest with name, 192/512 px icons, `start_url` and a `display` mode, plus engagement heuristics — **no service worker is required to install** ([web.dev, updated 2024-09-19](https://web.dev/articles/install-criteria)); you still need one for offline. `.wbix` double-click open requires manifest `file_handlers` + `launchQueue.setConsumer()`, **Chrome/Edge 102+, Firefox/Safari no**, unpolyfillable, with a per-open permission prompt ([Chrome docs](https://developer.chrome.com/docs/capabilities/web-apis/file-handling), [BCD](https://github.com/mdn/browser-compat-data/blob/main/manifests/webapp/file_handlers.json)). Custom title bar needs `window-controls-overlay` (Chrome 105+). Web Share Target is Chrome 89+/Android 76, Firefox no, Safari no ([MDN](https://developer.mozilla.org/en-US/docs/Web/Progressive_web_apps/Manifest/Reference/share_target)) — irrelevant for `.wbix`, keep `file_handlers` instead. Drag-drop-to-open works everywhere: handle `dragover`/`drop` on `window` and `preventDefault`.

**Recommendation:** one PWA build; SW precache the shell; `file_handlers` + `launchQueue` as a Chromium-only enhancement layered over drop/open-picker.
**Confidence: high.**

## 5) Prior art and licenses

- **Excalidraw** — MIT, 134.9k stars ([repo](https://github.com/excalidraw/excalidraw)): borrow the element model, perfect-freehand integration and export pipeline.
- **tldraw** — **custom license, "Not to use the Software in Production Environments"** without a key ([LICENSE.md](https://github.com/tldraw/tldraw/blob/main/LICENSE.md)): ideas only, no code.
- **drawnix** — MIT (plait-board/drawnix); **drawio** — Apache-2.0 (Electron-heavy); **whitebophir** — AGPL-3.0 (avoid for a closed product).
- Libraries: perfect-freehand MIT, fflate MIT, zip.js BSD-3, comlink Apache-2.0, roughjs stale (4.6.6, 2023).

**Recommendation:** stay on the MIT/BSD set — Excalidraw patterns + perfect-freehand + fflate — and never vendor tldraw.
**Confidence: high.**

**Unverified:** Uno/Avalonia WASM payload sizes; WinUI 3 corner-radius constants; iOS Apple-Pencil pressure behaviour; any Microsoft Whiteboard web engineering note; all performance numbers are vendor/blog claims, not measured here.
