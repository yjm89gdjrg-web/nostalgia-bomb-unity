# Nostalgia Bomb — Unity 6 iPhone offline greybox

**SOURCE PROTOTYPE — NOT compiled in Unity, NOT play-tested, NOT exported to Xcode, NOT an IPA.**

Standalone project targeting **Unity 6000.0.60f1 (Unity 6 LTS)** and iPhone landscape. No proprietary assets, CF branding/maps, networking, accounts, ads, purchases or server calls. Everything in the arena is original procedural geometry. Opening the project can require internet for Unity Package Manager's first test-framework download; gameplay itself has no network dependency.

## Open / run

1. Add this directory as a project in Unity Hub using Unity 6 LTS; allow package import and C# compilation to complete.
2. Open **`Assets/Scenes/Prototype.unity`**, then press Play. The saved scene already references `MatchGame` through a committed GUID, and is enabled in build settings.
3. If your editor requests scene upgrade or settings repair, use **Nostalgia Bomb → Create or refresh prototype scene**. This saves an actual bootstrap scene, not just an unsaved object. A runtime fallback also creates the bootstrap if another empty scene is played.
4. **Nostalgia Bomb → Configure iPhone landscape** sets legacy Input Manager (`activeInputHandler: 0`), landscape, IL2CPP, ARM64 and minimum iOS 15. Restart the editor if Unity asks after changing input backends. There are no named input axes: controls use `Input.GetKey`, pointer position and `Input.GetTouch`.

Minimal ProjectSettings are committed; Unity populates other defaults on import. There is no pre-generated Library directory or imported asset cache.

## Controls

- iPhone: lower-left floating joystick; drag free right-hand screen area to look; hold **FIRE**; tap **RELOAD / SWAP**; hold **USE** while stationary to plant. Independent touch IDs support moving/looking/firing together. Buttons and HUD use `Screen.safeArea`.
- Desktop: **WASD**, **right mouse drag** or arrow keys to look, **left mouse** to fire, **R** reload, **Q** swap rifle/pistol, hold **E** to plant. On-screen action boxes also accept mouse clicks.
- Blue attacks every round: player + 2 bots vs 3 red defensive bots. No side swapping or player-controlled defense in this first scope.
- You initially carry the bomb. Carry it to orange **A** or blue **B**, stop and hold use continuously for 3 seconds. Moving, releasing or dying cancels progress.
- On death, a carrier drops the bomb at their location; a living attacking teammate recovers automatically within 1.2 m. Defenders cannot pick it up. Bots can recover and plant independently.
- Warmup 3 s; live clock 95 s; planted fuse 35 s; continuous defensive defuse 5 s. Deadline wins ties against late planting/defusing.
- Attacker elimination wins for defense **only before planting**. After planting, surviving defenders must defuse before the fuse ends, even if all attackers are dead. Defender elimination wins for attack. Round scores settle once; all six respawn after 5 s.
- If you die, the camera stays at your death position and bots finish the round; there is no free spectator camera or respawn until next round.

## Included implementation

- `RoundRules.cs`: pure C# phase machine; carrier ownership/recovery, interruptible interactions, deadline ordering, elimination and single settlement. Unity validates proximity and site eligibility before passing interaction IDs.
- `Arena.cs`: original 32 × 40 m Split Foundry, perimeter, central workshop, staggered side doglegs, site covers and A/B markers. Collision-tested waypoint grid connects nearby nodes by clear sphere casts; Dijkstra routes around solid geometry. Empty paths never fall back to moving through walls.
- `Combatant.cs`: CharacterController movement, HP, raycast rifle/pistol, separate magazine/reserve counts, timed reloads, aim and short tracers. Bots use lower-frequency LOS decisions, waypoint routes and local separation; the designated defuser commits while other bots cover. Carriers prioritize planting; escorts follow whichever site the player approaches. Friendly bodies block shots but receive no damage.
- `MatchGame.cs`: saved/runtime bootstrap, round reset, bot objectives, bomb proxy, HUD status and win handoff. Simulation delta is capped on resume to avoid consuming the full fuse after app backgrounding. Not intended as deterministic/replay simulation.
- `TouchControls.cs`: legacy multitouch/mouse/key input and immediate-mode safe-area HUD; no Input System/EventSystem required.
- `Resources/Greybox.shader`: original tiny shader in Resources to avoid runtime `Shader.Find` references being stripped from mobile builds; no render-pipeline package required. Weapon appearance is one unanimated proxy even when pistol is selected; HUD/ammo/damage/fire cadence change correctly.
- `Editor/PrototypeMenus.cs`: saved scene generator, iPhone configuration and optional Xcode export menu.
- `Tests/EditMode/RoundRulesTests.cs`: 14 NUnit specifications for the pure round seam approved in the design: carrier drop/recovery, valid roles, interrupted/switching interactions, pre/postplant elimination, defuse, clocks/ties/large steps, single scoring/reset and invalid time/team arguments.

## Validation

On this Linux delivery host, **no Unity executable or C# compiler (`dotnet`, `csc`, `mcs`) was available**. No tools were installed. Therefore C# compilation, NUnit execution, Unity scene import, actual physics/bot matches, device controls and iOS output remain **unverified**.

Executed `python3 Tools/verify_source.py`: JSON/asmdef parsing, C# delimiter checks, namespace consistency, complete/unique asset GUIDs, saved-scene script linkage, build-scene linkage and legacy input selection passed. A separate sampled 2D footprint approximation found **68 connected waypoint nodes**, all six spawns and both sites connectable, and direct routes through the workshop/dogleg blocked. This is a geometry sanity check, **not Unity physics validation and not NUnit execution**.

Run actual tests after importing:

- **Window → General → Test Runner → EditMode → Run All**, assembly `NostalgiaBomb.Tests`.
- Optional CLI on a machine with Unity installed:
  ```sh
  /path/to/Unity -batchmode -nographics -projectPath /absolute/path/to/nostalgia-bomb-unity \
    -runTests -testPlatform EditMode -testResults /absolute/path/to/TestResults.xml \
    -logFile /absolute/path/to/tests.log
  ```
  Check process exit status and XML results; source presence is not a passing test result.

### Manual play-test checklist before claiming playable

- Compile/import cleanly; open saved scene and see one active camera/HUD and six actors.
- Walk both lanes: collision walls should block player/bots; bot paths should turn around doglegs and not stall due to controller crowding.
- Both weapons hit visible enemies, do not hit through world geometry, reload and swap properly; teammates block shots without damage.
- Kill the carrier without killing every attacker; verify dropped proxy, bot recovery and planting.
- Player plants at either A/B; release/move mid-hold resets progress. Bots should cover and a living defender should path to the bomb and continuously defuse.
- Wipe attackers before/after planting; verify different outcomes. Wipe defenders, time out, detonate and defuse; ensure exactly one score increment and six reset actors.
- On a real iPhone, check simultaneous joystick/look/fire, hold-use duration, notch/safe-area layouts, background/resume and frame time.

## iOS export / unsigned CI / blockers

Use **Nostalgia Bomb → Export iOS Xcode project** with Unity iOS Build Support available. A successful Unity export is an Xcode project, **not a signed/installable IPA**. The menu deliberately does not provide signing credentials or contact any build service.

### Manual GitHub Actions workflow — prepared locally, NEVER run

`.github/workflows/unity-ios-export.yml` has **only `workflow_dispatch`**, not a push trigger. It assumes this project is the repository root of an **authorized public repository with Actions enabled**. Only standard GitHub-hosted `ubuntu-latest` and `macos-latest` runners are specified: no larger/private paid runner assumption, external build service, purchase or new repository. Standard hosted runners for public repositories are GitHub's free usage route, subject to platform/account restrictions and runner availability; this does not establish that any account is currently eligible. Nothing here changes repository visibility, creates a repository or bypasses an account suspension.

1. **Required license preflight:** repository secrets `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` must all be present and non-whitespace; values are never echoed. `UNITY_LICENSE` must be a legitimate Unity license supported by GameCI for Unity **6000.0.60f1**, with credentials the owner is entitled to use. Presence is not proof of validity: activation/export may still fail. No license is supplied, no credentials were accessed, and no license acquisition/activation was attempted. Do not assume that any Unity Personal account automatically supplies a usable CI license file; confirm the supported licensing route first.
2. **Unity export on Ubuntu:** existing GameCI builder invokes `NostalgiaBomb.Editor.CiBuild.ExportIOS`; it requires actual `Unity-iPhone.xcodeproj` output. `NostalgiaBomb-Xcode-NOT-IPA` contains a tar.gz project archive so executable build-script permissions survive artifact transport. Its `EXPORT-ONLY-NOT-IPA.txt` describes this export stage only.
3. **Xcode build on macOS:** dependent job downloads that same run's artifact and records the runner's selected Xcode version/SDK. It builds scheme `Unity-iPhone`, configuration `Release`, SDK `iphoneos`, destination `generic/platform=iOS`, ARM64, with `CODE_SIGNING_ALLOWED=NO`, `CODE_SIGNING_REQUIRED=NO` and empty signing/team/profile settings. No certificate, provisioning secret, signing service or `-allowProvisioningUpdates` is used. `pipefail` preserves Xcode failures even with log capture. The selected runner Xcode/Unity toolchain compatibility is **not yet verified**.
4. **Unsigned packaging:** `Tools/package_unsigned_ios.sh` locates exactly one top-level `.app` in `Release-iphoneos` under DerivedData rather than guessing its product name; missing/ambiguous products fail closed. It checks binary/XML `Info.plist`, bundle ID, source marketing version, nonempty numeric build version, minimum OS, device platform, executable permissions and actual ARM64 Mach-O device-platform headers; simulator binaries and signing/provisioning material are rejected. It preserves file modes/internal symlinks, rejects escaping links, packages a single `Payload/<actual-name>.app/` and reopens the ZIP for CRC/layout/plist/executable validation. Expected marketing version comes independently from `ProjectSettings.asset`, not the app. The optional CLI build number adds exact build matching if desired; the workflow validates its format but does not assert an independent build number.
5. **Artifacts:** only successful validation uploads `NostalgiaBomb-UNSIGNED-IPA-NOT-INSTALLABLE`, containing `NostalgiaBomb-UNSIGNED.ipa` and a SHA-256 checksum; available Xcode/validation logs upload even if building/packaging fails. Retention is seven days. No upload to a game server, release publication or device-install step exists.

**Current blocker: no usable Unity CI license/credentials are configured or verified. No remote push, CI run, Unity/Xcode compilation, real game IPA or iPhone test occurred during this local implementation.** The local host has no Xcode. These stages are prepared code, not evidence that a cloud build succeeds.

**An UNSIGNED IPA cannot directly install or launch on a normal iPhone.** It is an intermediate archive for subsequent legitimate Apple signing/provisioning and authorized installation; re-signing must cover embedded frameworks and use matching app identifier/entitlements/profiles. That signing/install route remains unimplemented and untested. ZIP/header checks are not Apple's signing validation, App Store validation or proof of gameplay.

### Manual GitHub Actions packaging from an HK Linux Unity export — prepared locally, NEVER run

The second workflow, `.github/workflows/package-unsigned-ios-from-archive.yml`, is the credential-free bridge for an authorized HK Linux machine:

- Unity and Unity iOS Build Support run on the HK machine; no Unity account, license, email or password is sent to GitHub-hosted runners.
- The workflow is **manual-only** (`workflow_dispatch`) and uses only standard `macos-latest`.
- It accepts two manual inputs: an HTTPS URL for the exact `.tar.gz` Xcode export and its 64-hex-character SHA-256 digest. The URL must not contain embedded credentials. The archive must be reachable by the hosted runner without a private-cookie/login flow.
- It downloads with HTTPS-only redirects, validates the digest before extraction, then uses `Tools/safe_extract_tar.py` rather than `tar -xzf`. Extraction fails closed on absolute/traversal paths, backslashes/NULs, duplicate names, device/FIFO/special members, escaping/broken symlinks, writes through symlinked parents, and size/member limits. Legitimate relative Unity/framework symlinks and hard links are retained; executable bits are preserved (setuid/setgid/sticky bits are stripped).
- `Tools/validate_xcode_export.py` requires exactly one real `Unity-iPhone.xcodeproj`, its `project.pbxproj`, and the export-only marker before `xcodebuild` runs. This shape check is not a Unity authenticity or compilation check.
- Xcode builds `Release` for generic `iphoneos`, ARM64, with signing explicitly disabled. Existing `Tools/package_unsigned_ios.sh` performs the app/Mach-O/plist/ZIP checks and creates `NostalgiaBomb-UNSIGNED.ipa`; the IPA and checksum upload only after successful validation. Download/extraction/toolchain/Xcode/package logs upload with `if: always()`.
- The workflow never signs, installs, publishes, uploads a release, or tests on a device. An unsigned IPA is not directly installable on a normal iPhone.

#### HK export and dispatch procedure

1. On the authorized HK machine, export an iOS **Xcode project only** with the committed Unity entry point (`NostalgiaBomb.Editor.CiBuild.ExportIOS`), for example:
   ```sh
   /path/to/Unity -batchmode -nographics -quit \\
     -projectPath /absolute/path/to/nostalgia-bomb-unity \\
     -buildTarget iOS \\
     -executeMethod NostalgiaBomb.Editor.CiBuild.ExportIOS \\
     -nostalgiaOutput /absolute/path/to/build/iOS/NostalgiaBomb-Xcode \\
     -logFile /absolute/path/to/build/unity-ios-export.log
   ```
   Confirm the output contains `Unity-iPhone.xcodeproj` and `EXPORT-ONLY-NOT-IPA.txt`. Do not upload a Unity `Library` directory or a signed IPA.
2. Create the archive from the **parent** of the export directory so the project is restored with one top-level directory and Unix modes survive transport:
   ```sh
   cd /absolute/path/to/build/iOS
   tar -czf NostalgiaBomb-Xcode-NOT-IPA.tar.gz NostalgiaBomb-Xcode
   shasum -a 256 NostalgiaBomb-Xcode-NOT-IPA.tar.gz
   ```
   Keep the `.tar.gz` unchanged after calculating the digest. If using a generic object store, use an HTTPS object URL with no URL credentials and make it downloadable by the GitHub runner; do not paste tokens into the URL.
3. In GitHub Actions, choose **Package unsigned iOS IPA from HK Unity export → Run workflow**, paste the HTTPS URL and the digest, and start it manually. The digest is visible in the workflow log only as the expected value; the URL is deliberately omitted from project logs. Dispatch inputs are not secrets, so treat the URL and archive as non-confidential unless the hosting service provides separate access control.
4. Download the successful `NostalgiaBomb-UNSIGNED-IPA-from-HK-export-NOT-INSTALLABLE` artifact and its checksum. Keep the `NostalgiaBomb-HK-export-build-logs` artifact for troubleshooting. A failed build can still provide logs; a failed digest/extraction/validation never reaches Xcode.

The archive workflow has no Unity entitlement preflight because it does not invoke Unity. It still depends on a valid, compatible Unity export from the authorized HK environment, a reachable HTTPS archive, a matching SHA-256, and a compatible Xcode project/toolchain. This implementation was not run on GitHub; no real archive was fetched and no Xcode build was performed here.

### Local packaging validation — synthetic apps only

```sh
python3 -B Tools/test_ios_packaging.py
bash -n Tools/package_unsigned_ios.sh
```

Executed on this Linux host: **22 tests passed** using temporary, synthetic `.app` fixtures with binary/XML plists and minimal Mach-O headers. They cover actual product-name discovery, valid Payload/plist contents, executable mode, symlinks/empty directories, thin/fat ARM64 and older iOS load commands, malformed headers, simulator exclusion/rejection, ambiguous/missing products, bundle/version mismatch, missing/invalid executables, code-signing/provisioning rejection, path safety and overwrite refusal. Fixtures contain **no runnable game code** and are cleaned up; they are not a delivered IPA. Workflow YAML parsing, all embedded Bash syntax and license preflight with empty/whitespace/present dummy values also passed. No real secrets were read. Unity's 14 NUnit tests remain **not executed**.

After a real Xcode build, the packaging CLI is:

```sh
bash Tools/package_unsigned_ios.sh \
  build/DerivedData/Build/Products \
  build/unsigned/NostalgiaBomb-UNSIGNED.ipa \
  com.originalprototypes.nostalgiabomb 0.1.0
# Optional fifth argument: independently expected CFBundleVersion.
```

Official references: [GameCI activation](https://game.ci/docs/github/activation/), [GameCI builder](https://game.ci/docs/github/builder/), [GitHub runner billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions), [GitHub artifact transport](https://docs.github.com/en/actions/using-workflows/storing-workflow-data-as-artifacts), [Apple command-line Xcode build reference](https://developer.apple.com/library/archive/technotes/tn2339/_index.html). These explain prerequisites, not a claim that this workflow has run.

Known prototype limitations: bots have simplistic tactics and no navmesh/dynamic obstacle replanning beyond route refresh/local separation; path queries allocate lists/arrays; immediate-mode HUD and temporary shot objects are not production-mobile optimized; no audio, animation, polished weapon models, buy menu, jump/crouch, matchmaking, multiplayer, persistence or full match-end screen. Actor-vs-actor crowding and balance need actual play-testing. Unity API/version compatibility and scene serialization require real editor confirmation.
