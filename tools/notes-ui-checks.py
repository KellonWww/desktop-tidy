"""Isolated browser checks: mocked native bridge; never touches real notes or desktop files."""
import argparse
import os
import json
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
MOCK = r"""
(() => {
  const listeners = [];
  const initial = { type: 'state', groups: [], total: 0, search: '', updated: '刚刚',
    feedback: '本地工作区', opacity: 100, dark: false, followsSystemTheme: true,
    notes: JSON.parse(localStorage.getItem('test-notes') || '[]'), notesError: '' };
  window.mock = { state: initial, commands: [], rejectNext: false, hold: false };
  const emit = data => listeners.forEach(listener => listener({data}));
  mock.emit = emit;
  window.chrome = {webview: {
    addEventListener: (_, listener) => listeners.push(listener),
    postMessage: message => {
      mock.commands.push(message);
      if (message.command === 'ready') return setTimeout(() => emit(initial), 10);
      if (message.command === 'setTheme') { initial.dark = message.value === 'true'; emit(initial); return; }
      if (message.command === 'toggleEdgeNotes') { initial.edgeNotesEnabled = initial.edgeNotesEnabled === false; emit(initial); return; }
      if (message.command === 'search') { initial.search = message.value; emit(initial); return; }
      if (message.command === 'configureEdgeNotes') { initial.edgeNotesDelay = message.delay; initial.edgeNotesScreen = message.screen; emit(initial); return; }
      if (message.command === 'pauseEdgeNotes') { initial.edgeNotesPaused = !initial.edgeNotesPaused; emit(initial); return; }
      if (!['saveNote', 'deleteNote', 'toggleTodo', 'togglePin', 'copyDraft', 'copyNote'].includes(message.command)) return;
      const respond = () => {
        if (mock.rejectNext) { mock.rejectNext = false; emit({type:'notesResult', request:message.request, success:false, error:'模拟磁盘写入失败', notes:initial.notes}); return; }
        const found = initial.notes.find(note => note.Id === message.id);
        if (found && message.command !== 'copyNote' && found.Updated !== message.revision) {
          emit({type:'notesResult', request:message.request, success:false, error:'便签已在另一处更新，请重新打开编辑；当前草稿仍保留。', notes:initial.notes}); return;
        }
        if (message.command === 'saveNote') {
          const note = { Id: found?.Id || crypto.randomUUID().replaceAll('-', ''), Title:message.title,
            Text:message.text, IsTodo:message.todo === 'true', IsPinned:Boolean(found?.IsPinned), Completed:message.todo === 'true' && Boolean(found?.Completed), Updated:new Date().toISOString() };
          if (found) initial.notes[initial.notes.indexOf(found)] = note; else initial.notes.push(note);
        }
        if (message.command === 'toggleTodo') { found.Completed = !found.Completed; found.Updated = new Date().toISOString(); }
        if (message.command === 'togglePin') { found.IsPinned = !found.IsPinned; found.Updated = new Date().toISOString(); }
        if (message.command === 'deleteNote') initial.notes = initial.notes.filter(note => note.Id !== message.id);
        localStorage.setItem('test-notes', JSON.stringify(initial.notes));
        emit({type:'notesResult', request:message.request, success:true, notes:initial.notes}); emit(initial);
      };
      if (mock.hold) mock.release = respond; else setTimeout(respond, 20);
    }
  }};
})();
"""

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--screenshots', type=Path)
    parser.add_argument("--build-dir", type=Path, default=ROOT / os.environ.get("BUILD_DIR", "build/notes-dpi-fixed"))
    args = parser.parse_args()
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page(viewport={'width': 1180, 'height': 800}, device_scale_factor=1)
        errors = []
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.add_init_script(MOCK)
        page.goto((args.build_dir.resolve() / 'Web/index.html').as_uri())
        page.locator('#notes-nav').click()
        page.locator('#notes-edge-settings summary').click()
        page.locator('#edge-notes-toggle').click()
        expect(page.locator('#edge-notes-toggle')).to_have_attribute('aria-pressed', 'false')
        page.locator('#edge-notes-toggle').click()
        expect(page.locator('#edge-notes-toggle')).to_have_attribute('aria-pressed', 'true')
        page.keyboard.press('Escape')
        expect(page.locator('#notes-edge-settings')).not_to_have_attribute('open', '')
        expect(page.locator('#notes-view')).to_be_visible()
        expect(page.locator('#grid')).to_be_hidden()
        expect(page.locator('#notes-empty')).to_be_visible()
        page.locator('#new-note').click()
        page.locator('#note-save').click()
        expect(page.locator('#note-error')).to_contain_text('至少写一项')
        title = '常用回复 <script>alert(1)</script>'
        text = '你好，已收到。\n稍后回复 & 保持专注 😀'
        page.locator('#note-title').fill(title)
        page.locator('#note-text').fill(text)
        page.locator('#note-save').click()
        expect(page.locator('#note-editor')).not_to_be_visible()
        expect(page.locator('.note-title')).to_have_text(title)
        expect(page.locator('.note-body')).to_have_text(text)
        assert page.locator('.note-card script').count() == 0
        page.get_by_role('button', name='复制便签文本', exact=True).click()
        expect(page.locator('#notes-status')).to_have_text('已复制到剪贴板')
        page.locator('#new-note').click()
        page.locator('#note-title').fill('整理桌面')
        page.locator('#note-text').fill('把今天要做的事情写下来。')
        page.locator('#note-todo').check()
        page.locator('#note-save').click()
        expect(page.locator('.note-card')).to_have_count(2)
        page.locator('.note-check input').check()
        expect(page.locator('.note-card.completed')).to_have_count(1)
        page.locator('[data-note-filter="pending"]').click()
        expect(page.locator('.note-card')).to_have_count(0)
        page.locator('[data-note-filter="done"]').click()
        expect(page.locator('.note-card')).to_have_count(1)
        page.locator('.note-check input').uncheck()
        expect(page.locator('.note-card')).to_have_count(0)
        page.locator('[data-note-filter="all"]').click()
        expect(page.locator('.note-card')).to_have_count(2)
        todo = page.locator('.note-card').filter(has_text='整理桌面')
        todo.get_by_role('button', name='编辑便签', exact=True).click()
        page.locator('#note-text').fill('保存失败时也要保留这份草稿。')
        page.evaluate('mock.rejectNext = true')
        page.locator('#note-save').click()
        expect(page.locator('#note-error')).to_have_text('模拟磁盘写入失败')
        expect(page.locator('#note-text')).to_have_value('保存失败时也要保留这份草稿。')
        page.evaluate('mock.hold = true')
        page.locator('#note-save').click()
        expect(page.locator('#note-save')).to_be_disabled()
        page.keyboard.press('Escape')
        expect(page.locator('#note-editor')).to_be_visible()
        page.evaluate('mock.emit(mock.state)')
        expect(page.locator('#note-text')).to_have_value('保存失败时也要保留这份草稿。')
        page.evaluate('mock.hold = false; mock.release()')
        expect(page.locator('#note-editor')).not_to_be_visible()
        todo.get_by_role('button', name='删除便签', exact=True).click()
        page.locator('#note-delete-cancel').click()
        expect(page.locator('.note-card')).to_have_count(2)
        page.reload()
        page.locator('#notes-nav').click()
        expect(page.locator('.note-card')).to_have_count(2)
        expect(page.locator('#view-name')).to_have_text('便签')
        expect(page.locator('#notes-nav')).to_have_class('nav-item active')
        expect(page.locator('#all')).to_have_class('nav-item')
        expect(page.locator('.note-card').first).to_be_visible()
        # Periodic native state must not reset the current view or draft.
        page.evaluate('mock.emit(mock.state)')
        expect(page.locator('#view-name')).to_have_text('便签')
        if args.screenshots:
            args.screenshots.mkdir(parents=True, exist_ok=True)
            page.screenshot(animations="disabled", path=str(args.screenshots / '便签-20261023-light.png'))
        page.locator('#theme-toggle').click()
        expect(page.locator('html')).to_have_class('dark')
        if args.screenshots:
            page.screenshot(animations="disabled", path=str(args.screenshots / '便签-20261023-dark.png'))
        page.set_viewport_size({'width': 780, 'height': 620})
        assert page.evaluate('document.documentElement.scrollWidth <= innerWidth')
        if args.screenshots:
            page.screenshot(animations="disabled", path=str(args.screenshots / '便签-20261023-compact.png'))
        page.emulate_media(reduced_motion='reduce')
        assert page.locator('.note-card').first.evaluate("node => getComputedStyle(node).animationName") == 'none'
        page.locator('.note-card').first.get_by_role('button', name='删除便签', exact=True).click()
        page.locator('#note-delete-confirm').click()
        expect(page.locator('.note-card')).to_have_count(1)
        page.reload()
        page.locator('#notes-nav').click()
        expect(page.locator('.note-card')).to_have_count(1)
        page.evaluate("mock.state.notesError = '便签读取失败，原文件已保留'; mock.emit(mock.state)")
        expect(page.locator('#notes-status')).to_contain_text('原文件已保留')
        expect(page.locator('#new-note')).to_be_disabled()
        page.locator('#all').click()
        expect(page.locator('#notes-view')).to_be_hidden()
        expect(page.locator('#heading')).to_be_visible()
        assert not errors, json.dumps(errors, ensure_ascii=False)
        browser.close()
    print('PASS notes UI: CRUD, completion, filters, clipboard command, restart, XSS, failure/draft protection, themes, compact layout, reduced motion, corrupt-file protection, navigation; zero JS errors')

if __name__ == '__main__':
    main()
