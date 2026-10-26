"""Product interaction regression; isolated Chromium profile and mock bridge only."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("notes_checks", Path(__file__).with_name("notes-ui-checks.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-dir", type=Path, default=ROOT / os.environ.get("BUILD_DIR", "build/notes-dpi-fixed"))
    parser.add_argument("--screenshots", type=Path)
    args = parser.parse_args()
    url = (args.build_dir.resolve() / "Web/index.html").as_uri()
    with sync_playwright() as runtime:
        browser = runtime.chromium.launch()
        context = browser.new_context(viewport={"width": 1000, "height": 800})
        page = context.new_page()
        errors = []
        page.on("pageerror", lambda error: errors.append(str(error)))
        page.add_init_script(module.MOCK)
        page.goto(url)
        page.locator("#notes-nav").click()
        # Capture without a dialog; composition Enter must not commit partial Chinese input.
        page.locator("#quick-note").fill("中文输入中的想法")
        page.locator("#quick-note").dispatch_event("keydown", {"key": "Enter", "isComposing": True})
        assert not page.evaluate("mock.commands.some(m => m.command === 'saveNote')")
        page.reload(); page.locator("#notes-nav").click()
        expect(page.locator("#quick-note")).to_have_value("中文输入中的想法")
        page.locator("#quick-note").press("Enter")
        expect(page.locator(".note-card")).to_have_count(1)
        expect(page.locator("#quick-note")).to_have_value("")
        expect(page.locator("#quick-note")).to_be_focused()
        expect(page.locator("#note-editor")).not_to_be_visible()
        page.locator(".note-check input").check()
        expect(page.locator(".note-card")).not_to_be_visible()
        expect(page.locator("#notes-completed")).to_have_text("展开已完成（1）")
        page.locator("#notes-completed").click()
        expect(page.locator(".note-card")).to_be_visible()
        page.locator(".note-check input").uncheck()
        page.locator("#quick-todo").uncheck()
        page.locator("#quick-note").fill("常用文本 <script>alert(1)</script>")
        page.locator("#quick-note").press("Shift+Enter")
        page.locator("#quick-note").press("End")
        page.locator("#quick-note").press("Enter")
        expect(page.locator(".note-card")).to_have_count(2)
        page.locator(".note-card").last.get_by_role("button", name="置顶便签", exact=True).click()
        expect(page.locator(".note-card").first).to_have_class("note-card pinned")
        page.reload(); page.locator("#notes-nav").click()
        expect(page.locator(".note-card").first).to_have_class("note-card pinned")
        page.keyboard.press("Control+f")
        expect(page.locator("#notes-search")).to_be_focused()
        page.locator("#notes-search").fill("<script>")
        expect(page.locator(".note-card")).to_have_count(1)
        assert page.locator(".note-card script").count() == 0
        page.locator("#notes-search").fill("无匹配内容")
        expect(page.locator("#notes-empty")).to_be_visible()
        page.locator("#notes-search").fill("")
        # Editor drafts survive dismissal and full page reload; Ctrl+Enter saves.
        page.locator("#new-note").click()
        page.locator("#note-title").fill("草稿标题")
        page.locator("#note-text").fill("关闭和重启也不能丢")
        page.locator("#note-cancel").click()
        expect(page.locator("#note-drafts")).to_be_visible()
        page.reload(); page.locator("#notes-nav").click()
        page.locator("#note-restore").click()
        expect(page.locator("#note-text")).to_have_value("关闭和重启也不能丢")
        page.locator("#note-text").press("Control+Enter")
        expect(page.locator("#note-editor")).not_to_be_visible()
        expect(page.locator("#note-drafts")).not_to_be_visible()
        expect(page.locator(".note-card")).to_have_count(3)
        # A stale draft can be compared, copied, and saved as a new note safely.
        page.locator(".note-card").filter(has_text="草稿标题").get_by_role("button", name="编辑便签", exact=True).click()
        page.locator("#note-text").fill("我自己的修改")
        page.evaluate("const n = mock.state.notes.find(n => n.Title === '草稿标题'); n.Text = '另一个窗口的新版'; n.Updated = 'new-revision'; localStorage.setItem('test-notes', JSON.stringify(mock.state.notes)); mock.emit(mock.state)")
        expect(page.locator("#note-conflict")).to_be_visible()
        page.locator("#note-conflict summary").click()
        expect(page.locator("#note-latest")).to_contain_text("另一个窗口的新版")
        page.locator("#note-copy-draft").click()
        expect(page.locator("#notes-status")).to_have_text("草稿已复制到剪贴板")
        assert page.evaluate("mock.commands.findLast(m => m.command === 'copyDraft').text.includes('我自己的修改')")
        page.locator("#note-save-copy").click()
        expect(page.locator("#note-editor")).not_to_be_visible()
        expect(page.locator(".note-card")).to_have_count(4)
        assert page.evaluate("mock.state.notes.some(n => n.Text === '另一个窗口的新版')")
        assert page.evaluate("mock.state.notes.some(n => n.Text === '我自己的修改')")
        # Explicit discard is different from closing; no draft should reappear.
        page.locator("#new-note").click(); page.locator("#note-text").fill("主动放弃的内容")
        page.locator("#note-discard").click()
        page.reload(); page.locator("#notes-nav").click()
        expect(page.locator("#note-drafts")).not_to_be_visible()
        # Preferences operate through the bridge rather than silently changing UI only.
        page.locator("#notes-edge-settings summary").click()
        page.locator("#edge-delay").select_option("1000")
        page.locator("#edge-screen").select_option("primary")
        page.locator("#edge-pause").click()
        expect(page.locator("#edge-pause")).to_have_attribute("aria-pressed", "true")
        assert page.evaluate("mock.state.edgeNotesDelay === 1000 && mock.state.edgeNotesScreen === 'primary'")
        # A failed draft backup must not silently dismiss unsaved work.
        page.locator("#new-note").click()
        page.evaluate("() => { window.originalSet = Storage.prototype.setItem; Storage.prototype.setItem = function(k,v) { if (k.endsWith('.drafts')) throw new Error('test quota'); return originalSet.call(this,k,v); }; }")
        page.locator("#note-text").fill("写入失败也必须留在编辑器")
        page.locator("#note-close").click()
        expect(page.locator("#note-editor")).to_be_visible()
        expect(page.locator("#note-draft-status")).to_contain_text("备份失败")
        page.evaluate("() => { Storage.prototype.setItem = originalSet; }")
        page.locator("#note-cancel").click()
        # Edge/workspace drafts use independent keys; quick capture protects auto-collapse.
        edge = context.new_page(); edge.on("pageerror", lambda error: errors.append(str(error))); edge.add_init_script(module.MOCK); edge.goto(url + "?notesEdge=1")
        expect(edge.locator("#note-drafts")).not_to_be_visible()
        edge.locator("#quick-note").fill("抽屉专属草稿")
        edge.wait_for_function("() => mock.commands.filter(m => m.command === 'notesInteraction').at(-1).value === 'true'")
        edge.reload()
        expect(edge.locator("#quick-note")).to_have_value("抽屉专属草稿")
        edge.locator("#new-note").click(); edge.locator("#note-text").fill("抽屉编辑草稿"); edge.locator("#note-cancel").click()
        page.reload(); page.locator("#notes-nav").click(); page.locator("#note-restore").click()
        expect(page.locator("#note-text")).to_have_value("写入失败也必须留在编辑器")
        page.locator("#note-cancel").click()
        edge.locator("#quick-note").press("Enter")
        expect(edge.locator("#quick-note")).to_have_value("")
        expect(edge.locator("#quick-note")).to_be_focused()
        edge.wait_for_function("() => mock.commands.filter(m => m.command === 'notesInteraction').at(-1).value === 'false'")
        edge.set_viewport_size({"width": 360, "height": 500})
        assert edge.evaluate("document.documentElement.scrollWidth <= innerWidth")
        edge.set_viewport_size({"width": 440, "height": 760})
        if args.screenshots:
            args.screenshots.mkdir(parents=True, exist_ok=True)
            page.screenshot(path=str(args.screenshots / "notes-product-main.png"), animations="disabled")
            edge.screenshot(path=str(args.screenshots / "notes-product-edge.png"), animations="disabled")
        # Dock status is visible and only the dedicated drawer heading starts a drag.
        expect(edge.locator("#note-dock-location")).to_contain_text("左侧")
        edge.evaluate("mock.emit({...mock.state, edgeNotesDockSide: 'Right', edgeNotesDockStatus: '已吸附右侧 · 收起后留有便签把手'})")
        expect(edge.locator("#note-dock-location")).to_contain_text("已吸附右侧")
        edge.locator("#note-dock-drag").dispatch_event("pointerdown", {"button": 0})
        assert edge.evaluate("mock.commands.at(-1).command === 'move'")
        move_count = edge.evaluate("mock.commands.filter(m => m.command === 'move').length")
        edge.locator("#new-note").click()
        edge.locator("#note-dock-drag").dispatch_event("pointerdown", {"button": 0})
        assert edge.evaluate("mock.commands.filter(m => m.command === 'move').length") == move_count
        edge.locator("#note-cancel").click()
        # Separate edit/copy targets and a large, read-only focus surface for long notes.
        long_text = "完整阅读测试\n" + "长便签不应该挤在小框里。" * 100
        edge.locator("#quick-note").fill(long_text)
        edge.locator("#quick-note").press("Enter")
        card = edge.locator(".note-card").filter(has_text="完整阅读测试")
        expect(card).to_be_visible()
        preview = card.locator(".note-preview")
        assert card.locator(".note-body").bounding_box()["height"] < 70
        edit_box = card.get_by_role("button", name="编辑便签", exact=True).bounding_box()
        copy_box = card.get_by_role("button", name="复制便签文本", exact=True).bounding_box()
        assert edit_box["height"] >= 44 and copy_box["y"] >= edit_box["y"] + edit_box["height"] + 10
        preview.focus(); preview.press("Enter")
        expect(edge.locator("#note-reader")).to_be_visible()
        expect(edge.locator("#note-reader-body")).to_have_text(long_text)
        assert edge.locator("#note-reader textarea").count() == 0
        edge.wait_for_function("() => mock.commands.filter(m => m.command === 'notesInteraction').at(-1).value === 'true'")
        assert edge.locator("#note-reader-body").bounding_box()["height"] > 400
        if args.screenshots:
            edge.screenshot(path=str(args.screenshots / "notes-focus-reading.png"), animations="disabled")
        edge.keyboard.press("Escape")
        expect(edge.locator("#note-reader")).not_to_be_visible()
        edge.wait_for_function("() => mock.commands.filter(m => m.command === 'notesInteraction').at(-1).value === 'false'")
        preview.focus(); preview.press("Space")
        edge.locator("#note-reader-edit").click()
        expect(edge.locator("#note-reader")).not_to_be_visible()
        expect(edge.locator("#note-editor")).to_be_visible()
        expect(edge.locator("#note-text")).to_have_value(long_text)
        assert edge.locator("#note-text").bounding_box()["height"] >= 300
        assert edge.locator("#note-text").evaluate("e => parseFloat(getComputedStyle(e).fontSize)") == int(edge.locator("#note-editor-font-size").input_value())
        if args.screenshots:
            edge.screenshot(path=str(args.screenshots / "notes-focus-editing.png"), animations="disabled")
        edge.locator("#note-cancel").click()
        # Reading follows shared changes, including deletion, without writing or copying.
        preview.click()
        edge.evaluate("mock.state.notes = mock.state.notes.map(n => n.Text.startsWith('完整阅读测试') ? {...n, Text: '另一窗口更新的正文'} : n); mock.emit(mock.state)")
        expect(edge.locator("#note-reader-body")).to_have_text("另一窗口更新的正文")
        edge.evaluate("mock.state.notes = mock.state.notes.filter(n => n.Text !== '另一窗口更新的正文'); mock.emit(mock.state)")
        expect(edge.locator("#note-reader-title")).to_have_text("便签已删除")
        expect(edge.locator("#note-reader-edit")).to_be_disabled()
        edge.locator("#note-reader-close").click()
        for width, height in [(360, 500), (440, 760)]:
            edge.set_viewport_size({"width": width, "height": height})
            edge.locator(".note-preview").first.click()
            assert edge.locator("#note-reader").bounding_box()["width"] <= width
            assert edge.locator("#note-reader").bounding_box()["height"] <= height
            edge.locator("#note-reader-edit").click()
            assert edge.locator("#note-editor").bounding_box()["width"] <= width
            assert edge.evaluate("document.documentElement.scrollWidth <= innerWidth")
            edge.locator("#note-cancel").click()
        # Resting capture stays compact; focus explicitly gives writing space.
        edge.locator("#notes-search").focus()
        expect(edge.locator("#quick-note-form")).not_to_have_class("quick-note-form capture-expanded")
        compact_height = edge.locator("#quick-note").bounding_box()["height"]
        edge.locator("#quick-note").focus()
        expect(edge.locator("#quick-note-form")).to_have_class("quick-note-form capture-expanded")
        assert edge.locator("#quick-note").bounding_box()["height"] > compact_height + 80
        assert not errors, json.dumps(errors, ensure_ascii=False)
        browser.close()
    print("PASS notes product: quick capture/IME, draft recovery/isolation/failure, conflict comparison/copy/save-as, pin/search/folding, shortcuts, trigger preferences, focus reading/editing, separated targets")


if __name__ == "__main__":
    main()
