"""Isolated browser acceptance of inline editing, continued saving and note windows."""
import argparse
import importlib.util
import json
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("notes_checks", Path(__file__).with_name("notes-ui-checks.py"))
checks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checks)
NOTE = {"Id": "1" * 32, "Title": "验收标题", "Text": "第一行\n第二行 TODO 内容", "Updated": "v1", "IsTodo": False}


def main():
    """Run real browser input against an isolated bridge, not the user's notes."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-dir", type=Path, required=True)
    args = parser.parse_args()
    url = (args.build_dir.resolve() / "Web/index.html").as_uri()
    with sync_playwright() as runtime:
        browser = runtime.chromium.launch()
        context = browser.new_context(viewport={"width": 650, "height": 640})
        page = context.new_page()
        errors = []
        page.on("pageerror", lambda error: errors.append(str(error)))
        page.add_init_script("if (!localStorage.getItem('test-notes')) localStorage.setItem('test-notes', " + json.dumps(json.dumps([NOTE])) + ");" + checks.MOCK)
        page.goto(url + "?noteWindow=" + NOTE["Id"])
        expect(page.locator("#note-inline-body")).to_be_visible()
        # A genuine drag selection must remain in reading mode.
        bounds = page.locator("#note-inline-body").bounding_box()
        page.mouse.move(bounds["x"] + 4, bounds["y"] + 24)
        page.mouse.down(); page.mouse.move(bounds["x"] + 115, bounds["y"] + 24, steps=12); page.mouse.up()
        expect(page.locator("#note-editor")).to_have_class("note-reading")
        page.evaluate("getSelection().removeAllRanges()")
        page.locator("#note-inline-body").click()
        expect(page.locator("#note-text")).to_be_focused()
        page.locator("#note-text").fill("第一次保存")
        page.keyboard.press("Control+s")
        expect(page.locator("#note-draft-status")).to_have_text("已保存")
        expect(page.locator("#note-text")).to_be_focused()
        page.keyboard.type(" continued")
        page.keyboard.press("Control+s")
        expect(page.locator("#note-draft-status")).to_have_text("已保存")
        assert page.evaluate("mock.state.notes[0].Text") == "第一次保存 continued"
        assert page.evaluate("mock.state.notes.length") == 1
        print("PASS click-to-edit, selection, Ctrl+S focus and repeated save")
        # Failure must preserve both unsaved text and editing focus.
        page.locator("#note-text").fill("失败也不能丢失")
        page.evaluate("mock.rejectNext = true")
        page.keyboard.press("Control+s")
        expect(page.locator("#note-error")).to_contain_text("失败")
        expect(page.locator("#note-text")).to_be_focused()
        expect(page.locator("#note-text")).to_have_value("失败也不能丢失")
        page.locator("#note-text").dispatch_event("keydown", {"key": "s", "ctrlKey": True, "isComposing": True})
        assert page.evaluate("mock.commands.filter(m => m.command === 'saveNote').length") == 3
        print("PASS failed save and Chinese composition protection")
        for size in [(364, 301), (650, 640), (1200, 850)]:
            page.set_viewport_size({"width": size[0], "height": size[1]})
            expect(page.locator("#note-save")).to_be_in_viewport(ratio=1)
            assert page.locator("#note-window-dock").evaluate("e => e.scrollHeight <= e.clientHeight")
        page.locator("#note-window-topmost").click()
        assert page.evaluate("mock.commands.at(-1).command") == "toggleNoteWindowTopMost"
        page.locator("#note-window-dock").click()
        assert page.evaluate("mock.commands.at(-1).dirty") == "true"
        assert page.evaluate("mock.commands.at(-1).dock") == "true"
        page.evaluate("mock.emit({type:'noteWindowClosing'})")
        assert page.evaluate("mock.commands.at(-1).dirty") == "true"
        print("PASS compact layout, topmost and dirty-close bridge")
        # A stale editor must reject save, retain its text, then support save-as-copy.
        page.evaluate("mock.state.notes[0].Updated='external'; mock.state.notes[0].Text='外部内容'; mock.emit(mock.state)")
        expect(page.locator("#note-conflict")).to_be_visible()
        page.locator("#note-save").click()
        expect(page.locator("#note-error")).to_contain_text("另一处更新")
        page.locator("#note-save-copy").click()
        expect(page.locator("#note-draft-status")).to_have_text("已保存")
        assert page.evaluate("mock.state.notes.length") == 2
        opened = page.evaluate("mock.commands.filter(m=>m.command==='openNoteWindow').at(-1).id")
        assert opened != NOTE["Id"]
        print("PASS conflict protection and save-as-copy identity")
        # New-note first save must adopt its ID; a second Ctrl+S updates, not duplicates.
        page.goto(url)
        page.locator("#notes-nav").click(); page.locator("#new-note").click()
        page.locator("#note-text").fill("新便签")
        page.keyboard.press("Control+s")
        expect(page.locator("#note-draft-status")).to_have_text("已保存")
        count = page.evaluate("mock.state.notes.length")
        page.keyboard.type(" next"); page.keyboard.press("Control+Enter")
        expect(page.locator("#note-draft-status")).to_have_text("已保存")
        assert page.evaluate("mock.state.notes.length") == count
        assert page.evaluate("mock.state.notes.at(-1).Text") == "新便签 next"
        page.locator("#note-detach").click()
        expect(page.locator("#note-editor")).not_to_be_visible()
        assert page.evaluate("mock.commands.at(-1).command") == "openNoteWindow"
        print("PASS new-note identity, Ctrl+Enter and detach")
        # The same editor works in the edge surface and locks auto-hide only while editing.
        page.goto(url + "?notesEdge=1")
        page.locator(".note-body").first.click()
        expect(page.locator("#note-inline-body")).to_be_visible()
        page.locator("#note-inline-title").click()
        expect(page.locator("#note-title")).to_be_focused()
        assert page.evaluate("mock.commands.filter(m=>m.command==='notesInteraction').at(-1).value") == "true"
        page.keyboard.press("Escape")
        expect(page.locator("#note-editor")).not_to_be_visible()
        page.wait_for_function("mock.commands.filter(m=>m.command==='notesInteraction').at(-1).value === 'false'")
        assert not errors, errors
        print("PASS edge inline title editing and auto-hide lock; no JavaScript errors")
        browser.close()


if __name__ == "__main__":
    main()
