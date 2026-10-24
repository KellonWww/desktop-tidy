"""Edge-only browser regression checks; isolated mock bridge, no real desktop writes."""
import argparse
import os
import importlib.util
import json
import sys
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("notes_checks", Path(__file__).with_name("notes-ui-checks.py"))
module = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(module)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--screenshots", type=Path)
    parser.add_argument("--build-dir", type=Path, default=ROOT / os.environ.get("BUILD_DIR", "build/notes-dpi-fixed"))
    args = parser.parse_args()
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page(viewport={"width": 440, "height": 760})
        errors = []
        page.on("pageerror", lambda error: errors.append(str(error)))
        page.add_init_script(module.MOCK)
        page.goto((args.build_dir.resolve() / "Web/index.html").as_uri() + "?notesEdge=1")
        expect(page.locator("#notes-view")).to_be_visible()
        expect(page.locator(".sidebar")).to_be_hidden()
        expect(page.locator("#grid")).to_be_hidden()
        expect(page.locator("#resize")).to_be_hidden()
        expect(page.locator("#edge-notes-toggle")).to_be_hidden()
        page.locator("#new-note").click()
        page.wait_for_function("() => mock.commands.some(m => m.command === 'notesInteraction' && m.value === 'true')")
        page.locator("#note-title").fill("今天要做的事")
        page.locator("#note-text").fill("完成便签工作区\n保持桌面分类原样")
        page.locator("#note-todo").check()
        # Escape closes the editor only, never the edge panel.
        count = page.evaluate("mock.commands.filter(m => m.command === 'hide').length")
        page.keyboard.press("Escape")
        expect(page.locator("#note-editor")).not_to_be_visible()
        assert page.evaluate("mock.commands.filter(m => m.command === 'hide').length") == count
        page.locator("#new-note").click()
        page.locator("#note-title").fill("今天要做的事")
        page.locator("#note-text").fill("完成便签工作区\n保持桌面分类原样")
        page.locator("#note-todo").check()
        page.evaluate("mock.hold = true")
        page.locator("#note-save").click()
        page.keyboard.press("Escape")
        expect(page.locator("#note-editor")).to_be_visible()
        page.evaluate("mock.release(); mock.hold = false")
        expect(page.locator("#note-editor")).not_to_be_visible()
        page.wait_for_function("() => mock.commands.filter(m => m.command === 'notesInteraction').at(-1).value === 'false'")
        page.locator(".note-check input").check()
        expect(page.locator(".note-card.completed")).to_have_count(1)
        page.locator('[data-note-filter="done"]').click()
        expect(page.locator(".note-card")).to_have_count(1)
        page.get_by_role("button", name="复制便签文本", exact=True).click()
        expect(page.locator("#notes-status")).to_have_text("已复制到剪贴板")
        page.get_by_role("button", name="编辑便签", exact=True).click()
        page.locator("#note-text").fill("另一窗口更新时，这份草稿不能丢。")
        page.evaluate("mock.state.notes[0].Text = '来自另一个窗口'; mock.state.notes[0].Updated = 'new-revision'; mock.emit(mock.state)")
        expect(page.locator("#note-text")).to_have_value("另一窗口更新时，这份草稿不能丢。")
        page.locator("#note-save").click()
        expect(page.locator("#note-error")).to_contain_text("另一处更新")
        expect(page.locator("#note-text")).to_have_value("另一窗口更新时，这份草稿不能丢。")
        page.locator("#note-cancel").click()
        expect(page.locator(".note-body")).to_have_text("来自另一个窗口")
        page.keyboard.press("Control+f")
        expect(page.locator("#notes-view")).to_be_visible()
        assert page.evaluate("document.activeElement.id !== 'search'")
        page.keyboard.press("Escape")
        page.wait_for_function("() => mock.commands.some(m => m.command === 'hide')")
        page.locator('[data-command="hide"]').click()
        page.emulate_media(reduced_motion="reduce")
        page.wait_for_function("() => mock.commands.filter(m => m.command === 'notesMotion').at(-1).value === 'true'")
        # Reopening always shows all notes rather than silently preserving a completed-only filter.
        page.evaluate("mock.emit({...mock.state, type: 'notesOpened'})")
        expect(page.locator('[data-note-filter="all"]')).to_have_attribute("aria-pressed", "true")
        page.locator("#new-note").click()
        page.locator("#note-title").fill("常用文本")
        page.locator("#note-text").fill("把经常使用的内容放在这里，随时阅读、一键复制。")
        page.locator("#note-save").click()
        expect(page.locator(".note-card")).to_have_count(2)
        for width, height, theme in [(440, 760, "light"), (440, 760, "dark"), (360, 500, "compact")]:
            page.set_viewport_size({"width": width, "height": height})
            if theme == "dark": page.locator("#theme-toggle").click()
            assert page.evaluate("document.documentElement.scrollWidth <= innerWidth")
            assert page.locator("#new-note").bounding_box()["x"] >= 0
            if args.screenshots:
                args.screenshots.mkdir(parents=True, exist_ok=True)
                page.screenshot(animations="disabled", path=str(args.screenshots / ("便签边缘-20261023-" + theme + ".png")))
        page.locator("#notes-completed").click()
        for unused in range(2):
            page.get_by_role("button", name="删除便签", exact=True).first.click()
            page.locator("#note-delete-confirm").click()
        expect(page.locator(".note-card")).to_have_count(0)
        page.reload()
        expect(page.locator("#notes-view")).to_be_visible()
        expect(page.locator(".note-card")).to_have_count(0)
        forbidden = {"desktop", "arrange", "collapseAll", "assign", "resizeCategory", "hideEmpty", "move", "resize", "undo"}
        assert not forbidden.intersection(page.evaluate("mock.commands.map(m => m.command)"))
        assert not errors, json.dumps(errors, ensure_ascii=False)
        browser.close()
    print("PASS edge UI: notes-only, CRUD, completion, copy command, filters, editor/pending protection, shared-state conflicts, collapse, keyboard, compact layouts, motion, reload; zero JS errors")

if __name__ == "__main__":
    main()
