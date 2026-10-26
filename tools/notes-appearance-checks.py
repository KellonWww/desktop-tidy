"""Isolated syntax, striped rows, editor alignment and plain-text safety regression."""
import argparse
import importlib.util
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
    args = parser.parse_args()
    text = 'SELECT name FROM notes WHERE id = 42\nconst message = "hello"; // TODO keep\n`inline` **important** https://example.com\n<img src=x onerror=alert(1)>\n中文与空行\n\n尾行'
    with sync_playwright() as runtime:
        browser = runtime.chromium.launch()
        for edge in (False, True):
            page = browser.new_page(viewport={"width": 620 if edge else 1180, "height": 820})
            errors = []
            page.on("pageerror", lambda error: errors.append(str(error)))
            page.add_init_script(module.MOCK)
            page.add_init_script("""(() => {
              const send = chrome.webview.postMessage;
              chrome.webview.postMessage = message => {
                if (message.command !== 'noteFontSize') return send(message);
                const size = Number(message.value);
                if (Number.isInteger(size) && size >= 12 && size <= 32) mock.state.noteFontSize = size;
                mock.emit(mock.state);
              };
            })();""")
            page.goto((args.build_dir.resolve() / "Web/index.html").as_uri() + ("?notesEdge=1" if edge else ""))
            if not edge:
                page.locator("#notes-nav").click()
            page.locator("#new-note").click()
            page.locator("#note-title").fill("语法便签")
            page.locator("#note-text").fill(text)
            expect(page.locator("#note-highlight")).to_have_text(text + "\n")
            for kind in ("keyword", "string", "number", "comment", "code", "emphasis", "link"):
                assert page.locator("#note-highlight .note-syntax-" + kind).count() > 0, kind
            assert page.locator("#note-highlight img").count() == 0
            # A transparent native textarea retains all editing semantics, including IME.
            page.locator("#note-text").dispatch_event("keydown", {"key": "Enter", "ctrlKey": True, "isComposing": True})
            assert not page.evaluate("mock.commands.some(m => m.command === 'saveNote')")
            for dark in (False, True):
                page.evaluate("dark => { mock.state.dark = dark; mock.emit(mock.state); }", dark)
                assert page.locator("#note-highlight").evaluate("el => getComputedStyle(el).backgroundImage").startswith("repeating-linear-gradient")
                for prop in ("fontFamily", "fontSize", "lineHeight", "padding", "whiteSpace", "overflowWrap", "scrollbarGutter"):
                    assert page.evaluate("prop => getComputedStyle(document.querySelector('#note-text'))[prop] === getComputedStyle(document.querySelector('#note-highlight'))[prop]", prop), prop
            for size in (12, 24, 32, 16):
                page.locator("#note-editor-font-size").select_option(str(size))
                for control in ("#notes-font-size", "#note-reader-font-size", "#note-editor-font-size"):
                    expect(page.locator(control)).to_have_value(str(size))
                for target in ("#note-text", "#note-highlight", "#quick-note"):
                    assert page.locator(target).evaluate("el => getComputedStyle(el).fontSize") == f"{size}px"
                expect(page.locator("#note-text")).to_have_value(text)
            # Wrapping, blank lines and scrolling must remain aligned without changing input.
            long_text = ("SELECT 中文测试 " * 12 + "\n\n") * 7
            page.locator("#note-text").fill(long_text)
            page.locator("#note-text").evaluate("el => { el.scrollTop = el.scrollHeight; el.dispatchEvent(new Event('scroll')); }")
            assert page.evaluate("Math.abs(document.querySelector('#note-text').scrollTop - document.querySelector('#note-highlight').scrollTop) <= 1")
            page.locator("#note-text").fill(text)
            page.locator("#note-save").click()
            expect(page.locator("#note-editor")).not_to_be_visible()
            expect(page.locator(".note-body")).to_have_text(text)
            assert page.locator(".note-body .note-syntax-keyword").count() > 0
            page.locator(".note-preview").click()
            expect(page.locator("#note-reader-body")).to_have_text(text)
            page.locator("#note-reader-font-size").select_option("28")
            assert page.locator("#note-reader-body").evaluate("el => getComputedStyle(el).fontSize") == "28px"
            assert page.locator(".note-body").evaluate("el => getComputedStyle(el).fontSize") == "28px"
            expect(page.locator("#notes-font-size")).to_have_value("28")
            assert page.locator("#note-reader-body .note-syntax-keyword").count() > 0
            assert page.locator("#note-reader-body img").count() == 0
            assert page.evaluate("mock.state.notes[0].Text") == text
            page.emulate_media(forced_colors="active")
            assert page.locator("#note-reader-body").evaluate("el => getComputedStyle(el).backgroundImage") == "none"
            assert not errors, errors
            page.close()
        browser.close()
    print("PASS notes appearance: workspace + edge; font controls, tokens, stripes, themes, IME, scrolling, plain-text safety")


if __name__ == "__main__":
    main()
