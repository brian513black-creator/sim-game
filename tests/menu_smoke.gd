extends SceneTree

func _initialize() -> void:
    run.call_deferred()

func label(node: Node, value: String) -> Label:
    if node is Label and node.text == value:
        return node
    for child in node.get_children():
        var match_label = label(child, value)
        if match_label:
            return match_label
    return null

func click(node: Node, value: String) -> bool:
    if node is Button and node.text == value and not node.disabled:
        node.pressed.emit()
        return true
    for child in node.get_children():
        if click(child, value):
            return true
    return false

func slot_action(desk: Node, slot: int, action: String) -> bool:
    var heading = label(desk, "Campaign %d" % slot)
    return heading != null and click(heading.get_parent(), action)

func check(condition: bool, message: String) -> bool:
    if not condition:
        push_error(message)
        quit(1)
    return condition

func run() -> void:
    var legacy_path = "user://campaign.json"
    var other_path = "user://campaign-2.json"
    for path in [legacy_path, other_path, "user://campaign-3.json", "user://settings.cfg"]:
        if not check(not FileAccess.file_exists(path) and not FileAccess.file_exists(path + ".backup"),
                "Menu checks require an empty, isolated user-data directory."):
            return

    var legacy = FileAccess.open(legacy_path, FileAccess.WRITE)
    legacy.store_string('{"Version":6,"Seed":2029,"Calls":0,"Commands":[{"Kind":"pledge-budget"}],"Page":"Story"}')
    legacy.close()
    var original = FileAccess.get_file_as_string(legacy_path)
    var desk = load("res://Main.tscn").instantiate()
    root.add_child(desk)
    await process_frame
    if not check(label(desk, "Campaign 1") != null and click(desk, "How to play"), "Title menu opens the guide."):
        return
    if not check(label(desk, "Your first days in office") != null and click(desk, "Back to campaigns"), "Guide returns to campaign selection."):
        return
    var stale_menu = load("res://Main.tscn").instantiate()
    root.add_child(stale_menu)
    await process_frame
    if not check(slot_action(desk, 2, "New presidency") and click(desk, "Pledge worker relief · −5 capital"), "Start and play a second slot."):
        return
    if not check(FileAccess.get_file_as_string(legacy_path) == original and FileAccess.file_exists(other_path),
            "Starting a second slot preserves the legacy autosave."):
        return
    if not check(click(desk, "Menu") and slot_action(stale_menu, 2, "New presidency")
            and label(stale_menu, "Campaign 2 now has a save. Continue it or confirm replacement.") != null,
            "An older campaign menu cannot replace a newly created save by accident."):
        return
    stale_menu.free()
    if not check(slot_action(desk, 1, "Continue"), "Continue the first slot."):
        return
    if not check(label(desk, "Budget pledge pending. Sign an opening bill scored at $130B or less.") != null,
            "The first slot restores its own decision."):
        return
    if not check(click(desk, "Menu") and slot_action(desk, 2, "Replace campaign"), "Replacement requires confirmation."):
        return
    var found_dialog = false
    for child in desk.get_children():
        if child is ConfirmationDialog and child.visible:
            found_dialog = true
            child.hide()
    if not check(found_dialog and slot_action(desk, 2, "Continue"), "Canceling replacement keeps the second slot."):
        return
    if not check(label(desk, "Jobs pledge pending. Sign worker relief in the opening bill.") != null,
            "The second slot restores its own decision."):
        return
    if not check(click(desk, "Menu") and FileAccess.file_exists(other_path + ".backup"), "Second slot has its own recovery checkpoint."):
        return
    var damaged = FileAccess.open(other_path, FileAccess.WRITE)
    damaged.store_string("{")
    damaged.close()
    if not check(slot_action(desk, 2, "Continue")
            and label(desk, "The latest autosave was unreadable. Your previous checkpoint was restored.") != null
            and label(desk, "Jobs pledge pending. Sign worker relief in the opening bill.") != null,
            "A damaged second slot recovers its own prior decision."):
        return
    if not check(click(desk, "Menu") and click(desk, "Switch to fullscreen") and FileAccess.file_exists("user://settings.cfg"),
            "The display preference is saved."):
        return
    var unreadable_path = "user://campaign-3.json"
    var unreadable = FileAccess.open(unreadable_path, FileAccess.WRITE)
    unreadable.store_string("broken save")
    unreadable.close()
    if not check(click(desk, "How to play") and click(desk, "Back to campaigns")
            and slot_action(desk, 3, "Continue") and label(desk, "Campaign 3") != null
            and label(desk, "Campaign 3 has no readable autosave. Its files were preserved.") != null
            and FileAccess.get_file_as_string(unreadable_path) == "broken save",
            "A slot with no readable checkpoint stays at the campaign menu."):
        return
    if not check(slot_action(desk, 2, "Replace campaign"), "Replace the existing second campaign."):
        return
    var confirmed = false
    for child in desk.get_children():
        if child is ConfirmationDialog and child.visible:
            child.confirmed.emit()
            child.hide()
            confirmed = true
            break
    var replacement = JSON.parse_string(FileAccess.get_file_as_string(other_path))
    if not check(confirmed and replacement != null and replacement.Commands.size() == 0,
            "Replacement starts with a fresh history."):
        return

    desk.free()
    var broken_replacement = FileAccess.open(other_path, FileAccess.WRITE)
    broken_replacement.store_string("{")
    broken_replacement.close()
    desk = load("res://Main.tscn").instantiate()
    root.add_child(desk)
    await process_frame
    var checkpoint = JSON.parse_string(FileAccess.get_file_as_string(other_path + ".backup"))
    if not check(checkpoint != null and checkpoint.Seed == replacement.Seed and checkpoint.Commands.size() == 0
            and slot_action(desk, 2, "Continue")
            and label(desk, "The latest autosave was unreadable. Your previous checkpoint was restored.") != null
            and label(desk, "What will you promise the country?") != null,
            "Recovering a replacement restores the new presidency instead of the old one."):
        return

    var other_window = load("res://Main.tscn").instantiate()
    root.add_child(other_window)
    await process_frame
    if not check(slot_action(other_window, 2, "Continue")
            and label(other_window, "Campaign 2 is open in another window or its files are unavailable.") != null
            and label(other_window, "Campaign 2") != null,
            "Two open windows cannot write the same campaign slot."):
        return
    if not check(click(desk, "Menu") and slot_action(other_window, 2, "Continue")
            and label(other_window, "What will you promise the country?") != null,
            "Returning to the menu releases the campaign for another window."):
        return
    print("Campaign menu smoke checks passed.")
    quit()
