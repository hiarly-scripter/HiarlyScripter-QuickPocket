# QuickPocket v1.0.12-test Release Notes

Status: multiplayer tested locally, not published.
Date documented: 2026-06-13

## Summary

v1.0.12-test closes the multiplayer marker asymmetry found after v1.0.11. The unified hide/restore pipeline from v1.0.11 remains intact, and v1.0.12 specifically confirms that TAB/minimap markers now hide correctly in both directions: host observing Player 2 and Player 2 observing host.

## Bugs fixed

- Fixed host-side TAB/minimap marker remaining visible when Player 2 pocketed an item.
- Preserved the v1.0.11 fix where Player 2 no longer sees markers for host-pocketed items.
- Confirmed invisible collision did not appear in the multiplayer test.
- Confirmed invisible damage did not appear in the multiplayer test.
- Confirmed marker ghost at the old origin did not appear after drop.
- Confirmed audio ghost/spam did not appear in the tested scenario.

## Host flow

When the host pockets an item, the marker is hidden for the host and for Player 2.

When Player 2 pockets an item, the host now also applies the observer-side marker hide for that remote pocketed item. The host still avoids duplicate remote restore on drop, preserving the authoritative drop position.

## Player 2 flow

Player 2 continues to pocket and drop via the multiplayer request/ACK path. The owner side hides its local marker on ACK, while observers receive the room state and hide their local marker representation.

## Marker/minimap

Validated:

- Host pockets item with marker: Player 2 does not see the yellow marker.
- Player 2 pockets item with marker: host does not see the yellow marker.
- Drop works without marker returning to the old origin.

## Collision, damage, and audio

The approved manual test did not reproduce:

- invisible collision;
- invisible damage;
- audio ghost;
- massive "Can not play a disabled audio source" spam.

## Intentional limitation

Saw/Blade/Hazard items remain blocked intentionally as a safety limitation. They should stay blocked until dangerous item behaviours can be audited and supported without invisible damage or hazard risk.

## Tested manually

- Host loaded QuickPocket v1.0.12.
- Player 2 loaded QuickPocket v1.0.12.
- Host pocket/drop worked.
- Player 2 pocket/drop worked.
- Marker hiding worked in both directions.
- Collision, damage, marker ghost, and audio spam were not observed.

## Artifacts

DLL:
- `C:\Users\Hiarly\.claude\PROJETOS\REPO\HiarlyScripter-QuickPocket\build\HiarlyScripter-QuickPocket.dll`
- Bytes: 91136
- SHA-256: 66147EC5C909AC1BBF07D33FBB7556BE9919984BF3FF9D07F96DE723FE4A7DC1
- FileVersion: 1.0.12.0
- ProductVersion: 1.0.12-test+12e1e56b298b2043090786451d1e279ee96c1b7b

Manual ZIP:
- `C:\Users\Hiarly\.claude\PROJETOS\REPO\HiarlyScripter-QuickPocket\QuickPocket-v1.0.12-manual.zip`
- Bytes: 36649
- SHA-256: CDABD77EA2EEA9C752B36B474F2BF418CAE2E77FB85514F9D06FF0F98AFCB037

## Publication

This release note is local only.

No push was performed.
No publication was performed.
Default profile was not changed.
