## VibeSkua v2.0.6

This release introduces script pinning, playlist reordering in the Script Scheduler, native Skua Nexus webhook alerts with live statistics reporting, and major performance optimizations for script search and options dialogs.

### What's Changed
- **Script Pinning**: Pin your favorite scripts to keep them anchored at the top of the Script Repository, with a dedicated "Pinned" category filter.
- **Script Search Performance**: Eliminated first-launch lag and window freezes using asynchronous cache loading and background processing.
- **Script Scheduler Queue Reordering**: Added Up and Down controls to easily reorder queued scripts in playlists.
- **Skua Nexus Webhook Integration**: Added native Skua Nexus webhook support alongside Discord, delivering automated dual alerts, character information, equipment/quest snapshots, and live bot statistics.
- **Script Options Account Disambiguation**: Window titles now display the active account name (`Script Options - [Account]`) for multi-client clarity.
- **Script Options Performance**: Resolved popup lag and freezes by fixing a startup race condition and streamlining option editor controls.
- **Account Manager Bulk Selection**: Added a "Select All" button to quickly toggle all accounts for batch operations.
- **Webhook Input UI Fix**: Constrained the webhook input box to prevent window overflow and ensure the "Set" button stays visible.
- **Script Loader Credits**: Updated community issue report contacts to `@Tato2/@lonewolf_12`.

---

## VibeSkua v2.0.5

Updates webhook reporting for live dashboard snapshots.

### What's Changed
- **Webhook Statistics & Account Snapshots**: Added automated character data, equipment, quest, and live bot statistics reporting.

---

## VibeSkua v2.0.4 (Hotfix)

This hotfix resolves a Manager crash when starting accounts with a script selected.

### What's Changed
- **Account Launch Stability**: Fixed a crash where launching accounts while auto-start or a script was selected caused the Manager to crash during background settings synchronization.
- **In-Place Account Refresh**: Settings updates from running client instances now smoothly sync with the Manager without disrupting account checkmarks or active launch queues.

---

## VibeSkua v2.0.3

This release resolves the initial login connection freeze, introduces a rich account context menu in the Manager, enables real-time cross-process sync between clients and Manager, and delivers redesigned theme-aware account tag badges.

---

### What's Changed

### 1. Multi-Client Tab Bar & Group Assignment
- Restored Tab Context Menu: Restored right-click context menu on client tabs to quickly add/remove running accounts from groups.
- Login Validation: Blocks adding unauthenticated placeholder accounts (e.g. "User 1") to groups, displaying a prompt to log in first.
- Settings Persistence: Group additions from client tabs now automatically register the account under ManagedAccounts in Skua.settings.json.

---

### 2. Account Manager Context Menu & Real-Time Sync
- Account Context Menu: Added right-click menu to accounts in both List and Grid views (Start, Start with Script, Add/Remove Group, Edit Tags, Open Folder, Delete).
- Cross-Process Live Sync: Added Windows named event (Global\VibeSkua.Settings.Changed) and FileSystemWatcher so changes made in client tabs reflect instantly in the Manager without restarting.
- Instant In-Place Refresh: Eliminated the need to switch tabs inside the Manager to see updated accounts or group memberships.

---

### 3. Account Tag Chips Redesign & Dynamic Theming
- Visible by Default: Enabled ShowTags = true by default so tags are visible without opening the edit dialog.
- Live Theme Synchronization: Bound tag chip brushes to the active theme; colors now update immediately when switching themes in Options or Manager.
- Redesigned Badge Styling: Revamped tag chips into modern translucent pills with soft 1px accent borders, hover highlights, and an integrated mini TagOutline icon.

---

### 4. Fix for Initial Login "Connecting to Server" Screen Freeze
- Bypassed Flash 3-Second Port 843 Timeout: Flash Player was timing out for 3 seconds trying to reach TCP port 843 before falling back to port 5588. Implemented Security.loadPolicyFile("xmlsocket://<ip>:<port>") in AS3 and preloaded all server socket policies in parallel as soon as the server list appears, eliminating the 3-second freeze.
- Removed Artificial Latency: Replaced a 1,000ms timer loop delay in connectToServer with immediate execution, reduced 1,000ms thread sleeps in ScriptServers.cs to 100ms, and skipped blocking HTTP calls when servers are already cached.
- Guarded Handshake COM Calls: Guarded Stats.GetSpace() at loginResponse with if (Flash.IsWorldLoaded) to prevent COM calls into uninitialized world objects, and added null-checks in AS3 _getObjectA to prevent runtime #1009 errors.

---

### Installation & Updates

- **Automatic Update (Existing Users)**: If you installed VibeSkua using `VibeSkua-win-Setup.exe`, the update will download automatically in the background and apply upon restarting the application.
- **VibeSkua-win-Setup.exe (New Installs)**: Recommended 1-click Windows installer with automatic desktop shortcuts and background auto-updates.
- **VibeSkua-win-Portable.zip**: Zero-install standalone archive. Extract to any directory and launch `VibeSkua.Manager.exe` or `VibeSkua.exe`.

---

### Credits

- **auqw/skua** — Original framework & botting API.
- **ArrowDev (BlackArrow on Discord)** — Early Avalonia port foundation.
- **l0newolf12** — Butler V5 Avalonia port foundation.
- **NinjaXz** — Complete Avalonia overhaul, army controls, stability engineering, and release pipeline.
