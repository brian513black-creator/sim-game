# The Mandate

Play fictional U.S. President Brian Banks beginning January 20, 2029. Manage the opening semiconductor shock, navigate the 2030 midterms, and aim for 270 electoral votes in 2032. The agenda moves from emergency relief to durable policy, with episodes about labor talks, trade agreements, audits, the civilian response to the fictional Aster Strait war, and the handover. Follow the same cast from Elena Ruiz’s first letter to the final transfer of power. The story reacts to the opening pledge, midterms, past decisions, and how the presidency ends. Jules’s relief case carries through an audit, a presidential debate, and both handovers. A Senate trial or global war can open additional scenes. In the Oval Office, conversations with Elena, Jules, Mercer, and Reed can change public trust, the electoral map, and the closing letter.

A defeat ends your presidency in January 2033. Reelection unlocks a second and final term through January 2037, with new crises, the 2034 midterms, and a 2036 election to choose your successor. Your economic and legislative record carries forward. Each new presidency generates a new Congress, shuffles selected events within story arcs, and gives trading partners and the opposition their own priorities. The campaign autosaves its setup and choices locally after each action. Senate conviction after impeachment can end the presidency early.

## Play

Extract the Windows x64 release ZIP and run **The Mandate.exe**. Keep the accompanying data folder beside the executable. The title screen has three independent campaign slots, an in-game guide, and a windowed/fullscreen toggle. An existing autosave appears as Campaign 1. Each open campaign holds its own slot for saving, and replacing a campaign creates a fresh backup for the new presidency. Use **Menu** or **Esc** while playing to switch campaigns or review a finished term.

- **Story** opens by default for a new presidency. Read each scene, respond to a conversation or policy choice, and use **Continue story** to reach the next event. You can revisit earlier scenes and use the other desks whenever the decision needs more detail.
- **Oval Office** shows the daily brief, the current story chapter, and the White House log. In the opening chip crisis, you can promise worker relief, pledge to keep the bill’s budget impact within $130B, or govern without a pledge. The first bill’s outcome settles that promise. Sign or veto bills, appoint a cabinet secretary, issue emergency orders, or start agency rulemaking.
- **White House Counsel** brings occasional improper offers from domestic and foreign interests to the Oval Office. You can refuse, report, accept, or let an offer expire. Accepting can go undisclosed, cost public support, or lead to House impeachment and a Senate trial. With all senators present, 67 votes are required for conviction.
- **Capitol Hill** shows the live House and Senate counts when there is a bill on the floor. Each bill has its own provisions, budget estimate, and deadline. Offer delegation funding and switch to reconciliation for eligible packages.
- **Situation Room** shows the current disruption. Trade talks are available when a partner can help. Other episodes have a timed decision in the Oval Office, with a later briefing that reports what happened. Some decisions let you favor powerful groups, walk away from mediation, or withhold a report for short-term political capital. Later hearings, public support, and state results can reflect those choices.
- **Pentagon**, available through the Situation Room, tracks readiness and commitments during the Aster Strait crisis. You can order civilian support, commit a protective presence, seek a limited authorization from Congress, or withdraw. The simulation tracks 48-hour reporting and a 60-day War Powers clock for qualifying commitments.
- **Great Powers**, available through the Situation Room and Pentagon, tracks diplomatic escalation with Russia, China, India, the United Kingdom, and France. Overlapping confrontations can trigger World War III. Crisis hotlines and a ceasefire summit offer ways to de-escalate, and the wartime cabinet can fund civilian relief.
- **Decision Desk** projects electoral votes from today’s state margins and ranks where a visit could help most. Select a state to see what drives its support and how a visit would change it. A visit costs 5 political capital, adds 3.4 points to that state, and can be used once per state per term. Aim for 270 electoral votes. On election night, reveal state calls and follow the electoral count. Then finish the term through Inauguration Day and review your record. If you won reelection, take the oath for your final term at the Decision Desk.

- **Campaign Record** lets you read the story scenes in order or switch to the full White House briefings. Use it to revisit Michigan’s first call, the midterm aftermath, election night, and the handover. You can also restart with confirmation.

Choose **Next decision** to advance directly to the next major event. The calendar stops at new story scenes, important developments, and when a bill needs a signature or veto. Autosave keeps a second checkpoint so a damaged save can fall back to the last readable one.

The first release uses synthetic legislators, an authored economic and institutional agenda, and statewide winner-take-all election results. Maine and Nebraska's congressional-district allocation rules are simplified into statewide totals.

## Develop

Install the .NET 8 SDK and the .NET edition of Godot 4.5.1. From the repository root:

```sh
dotnet build GeopoliticsSim.sln
godot --headless --path . --editor --import
godot --path .
```

Use the Godot .NET binary for the second command. To export, install the matching .NET export templates, create `exports/windows`, and run:

```sh
godot --headless --path . --export-release "Windows Desktop" "exports/windows/The Mandate.exe"
```

The pure C# rules live in `GeopoliticsSim.Core`. Godot dispatches day batches to a background worker and updates its controls when the batch completes.

## Release checks

Run the campaign simulation smoke test and the menu checks before exporting. The menu check uses a fresh user-data directory so it cannot touch an existing campaign.

```sh
dotnet run --project tests/CampaignSmoke.csproj
XDG_DATA_HOME="$(mktemp -d)" godot --headless --path . --script tests/menu_smoke.gd
```
