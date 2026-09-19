[简体中文](./privacy-policy.zh-CN.md) | [English](./privacy-policy.en-US.md)

# WindBoard Privacy Policy

**Effective date: September 19, 2026**
**Last updated: September 19, 2026**
**Policy version: 1.0**

This Privacy Policy applies to the Windows desktop application **WindBoard** (the "App"), developed and published by **Jerry Z07** ("we", "us"). It applies to all versions of the App distributed through any channel, including Microsoft Store and GitHub Releases, and to all markets in which the App is published.

## 1. Summary

- The App **requires no account** and never asks you for identity information.
- The App **does not collect or upload your personal information** and contains no advertising, analytics, or telemetry components.
- Your whiteboard content, settings, and diagnostics **stay on your own device**; we cannot access them.
- The App accesses the network only for **update checking/downloading** and for **web addresses that you explicitly provide**.

## 2. Information we do not collect

The App does not collect, store, or transmit to us any of the following:

- Identity information such as your name, email address, phone number, or postal address;
- Device location data;
- Camera, microphone, contacts, or messaging data (the App declares no such system capabilities);
- Usage statistics, feature-click tracking, device identifiers, or advertising identifiers;
- Your ink strokes, whiteboard content, imported images, or exported files (this content is written only where you choose, and only when you export it).

The App reads local files only when you explicitly select them in a file picker (importing WBIX/WBI/images) or when you specify a local path for a Dock shortcut or the camouflage feature. It does not scan or read files unrelated to its features.

## 3. Data stored only on your device

The App stores the data it needs locally to provide its features and to help diagnose problems. This data is **never uploaded automatically**:

| Location | Content | Purpose |
| --- | --- | --- |
| `settings.json` | Settings such as UI and ink preferences, Dock shortcut entries (including the web addresses and local paths you enter), update and camouflage preferences | Remember your custom configuration |
| `Logs\` | Runtime logs | Operational records and troubleshooting |
| `Logs\Crashes\` | Crash reports (exception type and stack trace, app version, process ID, processor architecture, .NET and Windows versions, install form and related directory paths) | Diagnose crash causes |
| `camouflage\` | A copy of the icon file you selected for the camouflage feature | Customize the App's appearance |
| `downloads\` | Update package download cache | Avoid repeated downloads |
| Any location you choose | Workspace files (`.wbix`), PNG/PDF exports, and imported files that you save yourself | Fully under your control |

Storage locations:

- **Microsoft Store (MSIX) and non-Store installed builds**: data is stored in `%LocalAppData%\WindBoard` (for MSIX builds, Windows redirects it to the package-private store);
- **Portable build**: data is stored in the `data\` folder next to the program; if that folder is not writable, the App falls back to `%LocalAppData%\WindBoard`.

You may inspect, copy, or delete these files at any time. Crash reports and logs are shared with the developer only if you decide to do so; the App never sends them automatically.

## 4. When the App accesses the network

All network requests use HTTPS and **contain no credentials or identifiers that could identify you** (the `User-Agent` header contains only the app name and version, for example `WindBoard/1.2.3`).

| Scenario | Target service | Trigger | Data sent |
| --- | --- | --- | --- |
| Checking for updates | `github.com` (release metadata of the WindBoard repository) | You trigger it manually, or it runs automatically after launch according to your settings (set "Automatically check for updates" to "Never" to disable) | App name and version in the request header; no personal data |
| Downloading an update | `github.com` or a mirror acceleration service | You confirm an update download | Same as above |
| Download source speed test | Same as above | When you run the download-source speed test manually in Settings (the discontinued legacy installer build ran it once on first launch) | Same as above |
| Fetching Dock link icons | The site of a web address you entered in the Dock | When you add a "link" type Dock shortcut, or when the App reloads the Dock (for example at startup) | Requests to that site's home page and icon address |
| Opening external links | Your default browser | When you click a link inside the App (for example a GitHub release page or a Microsoft Store page) | Handled by the browser under its own policy |

Notes:

- **Microsoft Store builds** are updated by Microsoft Store; the App does not send update-check or download requests to GitHub.
- Dock link icons are an optional enhancement: if you never add a "link" type shortcut, the App contacts no third-party websites.

## 5. Third-party services and data sharing

- We **do not** sell, rent, or otherwise share your personal information with third parties, because we do not hold it.
- **GitHub**: update checks and update downloads are served by GitHub (GitHub, Inc.). As a service provider, GitHub may log your IP address and request metadata under its own privacy policy. We are not involved in those logs and **cannot access them**.
- **Mirror acceleration services**: when direct access to GitHub is restricted, the App may download update packages through third-party mirrors (`gh-proxy.top`, `ghm.078465.xyz`). These mirrors are operated by independent third parties and may also log access. If you prefer not to use them, you can pin the official GitHub source in the App settings.
- **Microsoft Store**: installing, updating, and licensing the App through Microsoft Store are handled by Microsoft and are subject to Microsoft's own privacy policy.

## 6. Data security

- Because the App neither collects nor transmits your data, there is no server-side data store to secure.
- Local data is stored within the permissions of your current Windows user account and is protected by the operating system's account and file permission model.
- All network requests are transmitted over HTTPS.

## 7. Data retention, deletion, and your controls

- **Turn off network features**: set "Automatically check for updates" to "Never" in Settings; the App will then stop sending automatic update-check requests (a manual check still requires network access).
- **View and delete local data**: open the data folder described above to inspect or delete its contents. Deleting `%LocalAppData%\WindBoard` (or `data\` next to the program in the portable build) removes all local data.
- **Uninstall**: after uninstalling, Microsoft Store builds are cleaned up by the system, including package-private data; for the portable build, simply delete the program folder.
- **Feedback you provide**: if you share logs or crash reports via GitHub Issues, we handle only what you voluntarily disclose, and you may ask us to remove that content at any time.

## 8. Children's privacy

The App does not collect any personal information from children and does not knowingly collect data from children under 13. Because no personal information is collected at all, the App is safe for use by families.

## 9. Changes to this policy

- When the App adds or changes features that involve data handling, we will update this policy and revise the "Last updated" date above.
- Material changes will be communicated through an in-app notice or in the release notes. Continuing to use the App means you accept the updated policy.

## 10. Scope

This policy applies to the App and to all of its market versions. Microsoft's privacy policies do not apply to the App, and Microsoft does not provide a default privacy policy for it.

## 11. Contact us

If you have any questions, comments, or complaints about this policy, please contact us at:

- GitHub Issues: <https://github.com/Jerry-Z07/WindBoard/issues>
