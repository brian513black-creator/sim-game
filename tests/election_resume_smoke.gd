extends SceneTree

func _initialize() -> void:
    run.call_deferred()

func label(node: Node, value: String) -> Label:
    if node is Label and node.text == value:
        return node
    for child in node.get_children():
        var found = label(child, value)
        if found:
            return found
    return null

func click(node: Node, value: String) -> bool:
    if node is Button and node.text == value and not node.disabled:
        node.pressed.emit()
        return true
    for child in node.get_children():
        if click(child, value):
            return true
    return false

func resume(desk: Node, slot: int) -> bool:
    var heading = label(desk, "Campaign %d" % slot)
    return heading != null and click(heading.get_parent(), "Continue")

func check(condition: bool, message: String) -> bool:
    if not condition:
        push_error(message)
        quit(1)
    return condition

func wait_for_calendar(desk: Node) -> bool:
    for frame in range(180):
        await process_frame
        if label(desk, "Advancing the presidential calendar…") == null:
            return true
    return check(false, "Calendar advancement should finish.")

func seed_slot(path: String, fixture: String) -> bool:
    if not check(not FileAccess.file_exists(path) and not FileAccess.file_exists(path + ".backup"),
            "Election checks require an empty, isolated user-data directory."):
        return false
    var saved = FileAccess.get_file_as_string("res://tests/fixtures/" + fixture)
    var file = FileAccess.open(path, FileAccess.WRITE)
    if not check(not saved.is_empty() and file != null, "Election fixture must be readable and the slot writable."):
        return false
    file.store_string(saved)
    file.close()
    return true

func run() -> void:
    var election_path = "user://campaign.json"
    var oath_path = "user://campaign-2.json"
    if not seed_slot(election_path, "election-night.json") or not seed_slot(oath_path, "legacy-second-oath.json"):
        return

    var scene = load("res://Main.tscn")
    var desk = scene.instantiate()
    root.add_child(desk)
    if not check(resume(desk, 1) and label(desk, "Election night") != null
            and click(desk, "Open the Decision Desk") and click(desk, "Call next states")
            and label(desk, "8 of 51 races called") != null and label(desk, "Election night in Michigan") == null,
            "Partial calls should leave the result scene hidden."):
        return

    desk.free()
    desk = scene.instantiate()
    root.add_child(desk)
    if not check(resume(desk, 1) and label(desk, "8 of 51 races called") != null
            and click(desk, "Call all remaining states") and click(desk, "Continue governing")
            and label(desk, "Election night in Michigan") != null,
            "Resume the partial tally and return to the election story once all states are called."):
        return

    desk.free()
    desk = scene.instantiate()
    root.add_child(desk)
    if not check(resume(desk, 1) and label(desk, "Election night in Michigan") != null
            and label(desk, "ELECTION DECISION DESK") == null,
            "Closing after Continue governing should reopen the story."):
        return

    for turn in range(12):
        if label(desk, "A Call from the Other Office") != null:
            break
        if click(desk, "Sign into law"):
            continue
        if not check(click(desk, "Continue story"), "Advance from election night to the transition scene."):
            return
        if not await wait_for_calendar(desk):
            return
    if not check(label(desk, "A Call from the Other Office") != null and label(desk, "Reply by December 30.") != null
            and label(desk, "Decision window closes Dec 18.") != null
            and click(desk, "Release the full budget brief · 5 capital")
            and click(desk, "Publish the unresolved deadlines · −6 capital"),
            "Both the budget decision and the transition conversation should be available in Story."):
        return

    if not check(click(desk, "Menu") and resume(desk, 2) and click(desk, "Take the oath for your second term"),
            "A winning legacy save can start the second term in the same session."):
        return
    var save = JSON.parse_string(FileAccess.get_file_as_string(oath_path))
    var decisions = save.Commands.map(func(command): return command.Kind)
    if not check(save.Summary.Term == 2 and save.Calls == 0 and decisions[-1] == "second-term"
            and "enable-conversations" in decisions and "enable-extended-story" in decisions
            and "enable-defense-scopes" in decisions and "enable-locked-trade-terms" in decisions,
            "The upgraded rules and fresh election count should be saved at the second oath."):
        return

    for turn in range(80):
        if label(desk, "The Empty Chair") != null:
            print("Election resume and second-oath smoke checks passed.")
            quit()
            return
        if click(desk, "Sign into law"):
            continue
        if not check(click(desk, "Next decision"), "The second term should be able to progress to its story scenes."):
            return
        if not await wait_for_calendar(desk):
            return
    check(false, "The Empty Chair scene should appear in the legacy second term without reloading.")
