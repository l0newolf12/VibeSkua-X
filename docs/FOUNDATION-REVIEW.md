# Foundation review

Reviewed on 2026-10-23 against the current worktree, the local Avalonia 12.1.1 and FluentAvalonia 3.1.0 sources, and the supplied `Game4000r5` decompilation. This is a focused review of the desktop shell, Flash transport/loading, and retained engine integration boundaries; it is not a complete audit of every retained script API.

The original review below was observational. All seven findings have since been addressed in the working tree, preserving the public Skua call APIs and script entry points. The resolution evidence below was verified on 2026-10-23; the original findings remain as rationale.

## Resolution and verification

| Finding | Implemented resolution | Passing evidence |
| --- | --- | --- |
| 1. Flash XML transport | Shared `FlashXml` codec for returns and callbacks; invariant numbers, typed values, nested objects, sparse arrays, and explicit rest-array unwrapping. Legacy JSON/boolean strings remain strings until typed conversion. | `FlashChecks` tests en-US/fr-FR, nested/sparse values, escaping, empty callbacks and both argument shapes. The native fixture tests real boolean/number/null returns under fr-FR; live AS3 failure callbacks retain separate phase/reason arguments. |
| 2. Cancellation | Generic and raw calls rethrow cancellation; explicit session/run tokens cover ordinary workers as well as the legacy script thread. | Actual `FlashUtil` tests cancelled generic/string/raw calls and ordinary fallback. Two real managers stop polling scripts independently; disposal during pending startup waits for cancellation before releasing services. |
| 3. Inventory capacity | Category-aware misc capacity comes from `myAvatar.objData.iBagSlots`, matching `World.isHouseOrRegularFull`; category-less fallback remains zero. | Live native startup reported **Game4000r5.swf**, matching the supplied snapshot. The production AS3 inventory API passes eight cases against the original `InvCat`: capacity, separate pools, full bag/misc, partial stack, bank swap, counts, and category-less fallback. |
| 4. Failure classification | Recoverable `FlashErrorMessage` reports an operation without ending the session. Fatal runtime disconnects have a separate message; initialization/load failures release owned resources. | A loaded real game survives an invalid typed call with the same HWND/runtime and a subsequent successful call. Native fixtures verify fatal disconnect, partial initialization, and missing-SWF cleanup/error UI. |
| 5. Async loading | AS3 handles I/O/security errors, malformed/missing version fields, and game initialization errors. `GameSession` owns a cancellable 45-second deadline and rejects late callbacks. The bundled SWF was rebuilt with Flex 4.16.1, target player 28. | Real ActiveX plus a loopback HTTP server tests unavailable version data, malformed JSON, unavailable SWF, timeout, close while loading, and late callbacks. Hash checks tie checked-in AS3 sources and SWFs to tested output binaries. |
| 6. Session ownership | Explicit `GameSession` state and ownership survive temporary viewport detachment; permanent close disposes it. Avalonia retains native attachment/sizing responsibility. | Real deferred detach/reattach preserves the original container, Flash HWND, runtime, and Ready state. Failed-session reattachment does not restart/access disposed controls. Native popup/owned-dialog overlap and z-order checks pass over real Flash; closing them leaves it usable. |
| 7. Engine isolation | `EngineSession` owns services, messengers, bot globals, compiler registries, and hotkey commands. Captured context routes later callbacks; semantic compiler rewriting binds legacy Toolkit globals in scripts/includes to their owner. | Two actual `ScriptManager`/`ScriptInterface` graphs concurrently compile and run representative `ScriptMain` source/includes. Tests verify global ownership, distinct messages, independent stop/error/disposal, preserved compiler registrations, restart/cache reuse, cleanup exceptions, and cancellation during startup. |

Final validation passed: Release solution build (zero warnings/errors), foundation checks, headless shell/transport/hash checks, and the opt-in Windows/CleanFlash checks. Commands are in [README.md](../README.md). Native results are saved under Git-ignored `artifacts/shell-tests/`: `native-result.json`, `native-startup-failure.json`, `native-session-checks.json`, and `inventory-contract.json`. The checked-in fixture build script and manifests make source/binary provenance reviewable without checking in the decompilation.

Validation limits: the engine tests fake game/platform services; the live native check reaches the login screen without logging into an account. Inventory fixtures establish snapshot client logic, not server acceptance or all historical game versions. Native popup/dialog checks verify real window geometry/z-order, not every FluentAvalonia overlay control or monitor/DPI configuration. Use native popup surfaces or owned windows when covering Flash. In-process engine isolation does not isolate arbitrary third-party static state or unmodified precompiled libraries using process globals, and cannot forcibly terminate uncooperative code. The shell still hosts one account and has no scripting UI wired yet.

## Original findings

## 1. Correct the Flash XML transport - P1

**Evidence:** Executed a temporary probe against the actual [FlashUtil](../VibeSkuaX.Runtime.Windows/Flash/FlashUtil.cs) implementation, without creating an ActiveX control.

| Input | Observed result |
| --- | --- |
| Serialize `1.5` under `en-US` | `<number>1.5</number>` |
| Serialize `1.5` under `fr-FR` | `<number>1,5</number>` |
| Decode a Flash array containing number `42` and boolean `true` | String array elements `"42"` and `""`, losing the original value types |
| Decode a Flash object with a `score` property | `RuntimeBinderException`: an `ExpandoObject` does not support the dynamic indexer being used |
| Receive callback arguments `"alpha"` and `42` | One concatenated argument, `"alpha42"` |

`CallHandler` decodes the `<arguments>` wrapper as a value instead of decoding its children. `FromFlashXml` also processes array `<property>` wrappers as values. Numeric formatting/parsing and `Convert.ChangeType` use the current culture. The outgoing and incoming paths do not share a consistent value decoder.

**Improvement:** Introduce a small, independently testable XML value codec used by both call returns and callbacks. Use invariant numeric formatting/conversion, decode typed values inside property wrappers, preserve array indexes, and populate objects through `IDictionary<string, object?>`. Handle native boolean/null/undefined return elements explicitly.

**Compatibility constraint:** [Externalizer.call](../Skua.AS3/skua/src/skua/Externalizer.as) sends its rest arguments as one array, and existing consumers such as `ScriptInterface.HandleFlashCall` expect flattened logical arguments. Preserve that contract deliberately. Many existing AS3 callbacks return strings containing booleans or JSON; those string contracts must also continue to work. Do not assume every current boolean API is broken: the string-returning wrappers avoid the native-boolean decoding gap.

**Validation:** Round-trip primitives, nested objects/arrays, empty values, XML-escaped strings, sparse/indexed arrays, and the actual Skua callback wrapper. Test at least `en-US` and `fr-FR`. Include typed return conversion and existing JSON/string callback shapes.

## 2. Preserve cancellation through typed Flash calls - P1

**Evidence:** A probe supplied a cancelled `IScriptManager` to the actual `FlashUtil`, on a thread named `Script Thread`.

| Call | Observed result |
| --- | --- |
| `Call("isTrue", typeof(int))` | Throws `OperationCanceledException` |
| `Call<int>("isTrue")` | Returns `0`, swallowing the cancellation |

The cancellation check in the non-generic overload is outside its catch block, but the generic overload catches every exception and returns a default value. This undermines cooperative stop behavior for scripts that poll through typed Flash calls. [ScriptManager](../Skua.Core/Scripts/ScriptManager.cs) already relies on cancellation for stopping script execution.

**Improvement:** Let cancellation propagate through generic calls. Preserve existing fallback behavior only for failures where returning a default remains part of the API contract. Review the thread-name condition when connecting the engine so cancellation ownership is explicit rather than extended by copying that convention to new workers.

**Validation:** Cancelled generic and non-generic calls both propagate cancellation, while ordinary missing/invalid values preserve the agreed fallback behavior. Exercise a representative polling script's stop path.

## 3. Align inventory capacity access with the supplied game - P1

**Evidence:** [Inventory.inventoryMiscSlots](../Skua.AS3/skua/src/skua/api/Inventory.as) returns `cat.MISC_SLOTS`. A search of the supplied `references/decompiled game/Game4000r5/scripts/` found no `MISC_SLOTS` definition. In that snapshot, `InvCat.isFullFor` accepts the capacity as an argument, and `World.isHouseOrRegularFull` passes `myAvatar.objData.iBagSlots` to it.

[ScriptInventory.MiscSlots](../Skua.Core/Scripts/Inventory/ScriptInventory.cs) and `MiscFreeSlots` rely on this bridge result. The source therefore contains an incompatible member assumption for the supplied snapshot; the resulting live-game exception/default behavior was not exercised with a logged-in account.

**Improvement:** Derive capacity from the game-version contract rather than assuming this static member exists. Preserve the public `MiscSlots`/`MiscFreeSlots` APIs and category-less fallback. Confirm the active loaded game's version before applying snapshot-specific assumptions.

**Validation:** Add fixtures for category-aware inventory capacity, partly filled stacks, miscellaneous versus bag pools, and bank swaps using the supplied `InvCat`/`World` behavior. Keep evidence tied to the snapshot; this does not establish server-side acceptance or behavior in another game version.

## 4. Separate call failures from fatal session failures - P1

**Evidence:** `FlashUtil.Call` publishes `FlashErrorMessage` for conversion or COM-call errors, except for its `isKicked` special case. [FlashViewport.StartFlash](../VibeSkuaX.App/Controls/FlashViewport.cs) subscribes to every such message and calls `Fail`, which tells the user Flash could not start and then disposes the entire runtime.

When scripting is connected, a value conversion error or an unavailable game property can therefore tear down an otherwise running session and produce misleading CleanFlash installation guidance. The current shell has not connected the retained scripting engine yet, so the existing native smoke check does not exercise that integration path.

**Improvement:** Distinguish startup/runtime-fatal failures from recoverable operation failures. Keep cancellation separate from both. Report the relevant operation and exception through existing diagnostics, preserve a healthy runtime after an isolated failed call, and reserve teardown for failures that actually invalidate the session.

**Validation:** Inject a recoverable failed call into a loaded session and prove the same Flash control remains alive and usable. Separately verify that a genuinely fatal initialization failure still disposes everything and displays the appropriate error.

## 5. Handle asynchronous game-load failures - P1

**Evidence:** The version request and game SWF loader in [Main.as](../Skua.AS3/skua/src/skua/Main.as) register completion handlers, but no I/O/security error handlers for those requests. Version JSON parsing is also unguarded. The viewport's production startup has no bounded load deadline; the 35-second deadline exists only in the native test.

**Improvement:** Report version-request, malformed-version-response, and SWF-download failures through the existing AS3-to-host callback mechanism. Validate the required version fields and add a bounded, cancellable startup deadline in the session owner. Late callbacks after close/failure must not revive the session.

**Validation:** Version endpoint unavailable, malformed response, unavailable SWF, and close while loading. Each case should leave an actionable error or a cleanly closed session, without an indefinite loading state. Rebuild the bundled SWF when changing the AS3 implementation; reviewing the source alone does not verify the shipped binary's behavior.

## 6. Resolve session ownership before detachable tabs - P2

**Evidence:** In local Avalonia 12.1.1 source, `NativeControlHost` queues destruction when detached and can create a native control again on later attachment. Our `DestroyNativeControlCore` calls permanent `FlashViewport.Dispose`, and its next `CreateNativeControlCore` throws once `_disposed` is true.

This is compatible with the current single-window, single-use viewport. It is a design constraint for future content navigation/account tabs, not evidence that the currently tested resize path destroys the game.

**Improvement:** Decide explicitly whether switching tabs keeps a live host attached or whether game-session lifetime must be owned separately from a disposable view. Keep the implementation small and make permanent close distinct from temporary visibility/navigation. Represent lifecycle states independently of display text as those transitions grow.

**Validation:** Switch away and back after Avalonia's deferred destruction work has had time to run. Verify the intended session survives, or that deliberate recreation succeeds without disposed-control access, duplicate startup, or stale callbacks. Also test menus/dialogs over real Flash because its native surface is absent from headless renders.

## 7. Audit engine isolation before multi-account execution - P2

**Evidence:** The viewport already owns a private weak messenger, but [AddCommonServices](../Skua.Core/AppStartup/Services.cs) registers process-default weak/strong messengers. [ScriptInterface](../Skua.Core/Scripts/ScriptInterface.cs) directly assigns `StrongReferenceMessenger.Default`, and `ScriptManager` uses that default messenger and `Ioc.Default` at several execution/compilation points.

Constructing separate viewports or service providers does not by itself isolate these process-wide dependencies. With account-specific engines connected, state or script lifecycle messages could be routed to the wrong session unless this boundary is resolved. Cross-account execution has not been tested or implemented in the new shell.

**Improvement:** Make the engine isolation decision before adding account tabs. Identify session-owned services and messages, inject the relevant instances where feasible, and preserve compatibility for existing script entry points and globals. Do not assume that changing DI registration lifetimes alone fixes direct global access.

**Validation:** Two independent test sessions: starting, stopping, failing, and disposing one must not change the other's state or receive its messages. Representative existing scripts must still compile and run through the chosen composition model.

## Original verification and recommended sequence

The existing foundation, headless shell, and native Windows/Flash checks all passed during this review. They establish the already-covered options/navigation/input and native window/Flash lifecycle behavior. They do not establish complete script compatibility or cover the newly reproduced transport/cancellation defects.

The temporary probe and its JSON output are under Git-ignored `artifacts/foundation-review/`. These are review evidence, not permanent regression tests. Permanent tests should exercise the real transport and representative script contracts rather than duplicate implementation logic or only assert UI properties.

Recommended implementation batches:

1. Fix and test XML transport and cancellation while preserving public call/callback contracts.
2. Correct the inventory snapshot mismatch and failure classification; validate with representative game access before connecting the engine.
3. Add asynchronous startup failure reporting/deadlines and test close-during-load behavior.
4. Establish detachable-view ownership and engine isolation before expanding into multi-account controls.

The current custom title bar and direct WinForms hosting already have passing native geometry, caption, cleanup, and exit checks. Preserve that behavior while addressing the uncovered integration paths. Performance changes should follow measurements of startup and script/game-call costs; this review did not measure a rendering or allocation bottleneck.
