(() => {
  "use strict";
  const $ = (id) => document.getElementById(id);
  const bridge = window.chrome?.webview;
  const edgeMode = new URLSearchParams(location.search).get("notesEdge") === "1";
  let editingRevision = "", deletingRevision = "", lastInteraction = null;
  let notes = [], filter = "all", editing = "", deleting = "", pending = null, loadError = "";
  function node(tag, className, text) {
    const value = document.createElement(tag);
    if (className) value.className = className;
    if (text !== undefined) value.textContent = text;
    return value;
  }
  function button(text, label, click) {
    const value = node("button", "note-action", text);
    value.type = "button";
    value.setAttribute("aria-label", label);
    value.addEventListener("click", click);
    return value;
  }
  function showNotes(show) {
    if (edgeMode) show = true;
    document.body.classList.toggle("show-notes", show);
    $("notes-view").hidden = !show;
    $("notes-nav").classList.toggle("active", show);
    $("notes-nav").setAttribute("aria-pressed", String(show));
    $("all").classList.toggle("active", !show);
    $("view-name").textContent = show ? "便签" : "全部图标";
    if (show) $("scroll").scrollTop = 0;
  }
  function render() {
    const completed = notes.filter((note) => note.IsTodo && note.Completed).length;
    const todos = notes.filter((note) => note.IsTodo).length;
    $("notes-count").textContent = notes.length;
    $("notes-summary").textContent = `${notes.length} 条便签 · ${todos - completed} 项待完成`;
    $("notes-status").textContent = loadError || $("notes-status").textContent;
    document.querySelectorAll("[data-note-filter]").forEach((item) =>
      item.setAttribute("aria-pressed", String(item.dataset.noteFilter === filter)));
    const visible = notes.filter((note) => filter === "all" ||
      (filter === "text" && !note.IsTodo) || (filter === "pending" && note.IsTodo && !note.Completed) ||
      (filter === "done" && note.IsTodo && note.Completed));
    const active = document.activeElement;
    const focusedId = active?.closest("[data-note-id]")?.dataset.noteId;
    const focusedAction = active?.dataset.noteAction;
    const grid = $("notes-grid");
    grid.replaceChildren();
    for (const note of visible.slice().reverse()) {
      const card = node("article", "note-card" + (note.Completed ? " completed" : ""));
      card.dataset.noteId = note.Id;
      const header = node("div", "note-card-header");
      header.append(node("span", "note-kind", note.IsTodo ? (note.Completed ? "已完成" : "TODO") : "文本"));
      if (note.IsTodo) {
        const label = node("label", "note-check");
        const check = node("input");
        check.type = "checkbox"; check.checked = note.Completed;
        check.dataset.noteAction = "toggle";
        check.setAttribute("aria-label", (note.Completed ? "标记未完成：" : "标记完成：") + (note.Title || note.Text));
        check.addEventListener("change", () => request("toggleTodo", { id: note.Id, revision: note.Updated }));
        label.append(check, node("span", "", note.Completed ? "已完成" : "完成"));
        header.append(label);
      }
      card.append(header);
      if (note.Title) card.append(node("h2", "note-title", note.Title));
      if (note.Text) card.append(node("p", "note-body", note.Text));
      const actions = node("div", "note-actions");
      const copy = button("复制", "复制便签文本", () => request("copyNote", { id: note.Id }));
      copy.dataset.noteAction = "copy";
      const edit = button("编辑", "编辑便签", () => openEditor(note));
      edit.dataset.noteAction = "edit";
      const remove = button("删除", "删除便签", () => { deleting = note.Id; deletingRevision = note.Updated; $("note-delete-dialog").showModal(); syncInteraction(); });
      remove.dataset.noteAction = "delete";
      actions.append(copy, edit, remove); card.append(actions); grid.append(card);
    }
    $("notes-empty").hidden = visible.length > 0 || Boolean(loadError);
    $("notes-empty").querySelector("h2").textContent = notes.length ? "这里暂时没有便签。" : "留住一个想法，或安排一件小事。";
    $("notes-empty-create").textContent = notes.length ? "＋ 新建便签" : "＋ 写下第一条";
    setBusy();
    if (focusedId && focusedAction) {
      const target = [...grid.querySelectorAll("[data-note-id]")].find((item) => item.dataset.noteId === focusedId);
      target?.querySelector(`[data-note-action="${focusedAction}"]`)?.focus({ preventScroll: true });
      if (!target) document.querySelector(`[data-note-filter="${filter}"]`).focus({ preventScroll: true });
    }
  }
  function syncInteraction() {
    const active = Boolean(pending || $("note-editor").open || $("note-delete-dialog").open);
    if (edgeMode && active !== lastInteraction) {
      lastInteraction = active; bridge?.postMessage({ command: "notesInteraction", value: String(active) });
    }
  }
  function setBusy() {
    syncInteraction();
    document.querySelectorAll("#notes-view button:not([data-note-filter]), #notes-grid input, #note-form input, #note-form textarea, #note-form button, #note-delete-dialog button").forEach((item) => {
      item.disabled = Boolean(pending) || Boolean(loadError);
    });
    // Even a read-only/error state must allow closing dialogs.
    for (const id of ["note-close", "note-cancel", "note-delete-cancel"]) $(id).disabled = Boolean(pending);
    $("note-save").textContent = pending?.command === "saveNote" ? "正在保存…" : "保存便签";
  }
  function request(command, extra) {
    if (pending || loadError) return;
    if (!bridge) { $("notes-status").textContent = "便签需要在桌面应用中使用。"; return; }
    pending = { command, request: crypto.randomUUID() };
    $("note-error").textContent = "";
    $("notes-status").textContent = "正在处理…";
    setBusy();
    bridge.postMessage({ command, request: pending.request, ...extra });
  }
  function openEditor(note) {
    if (pending || loadError) return;
    editing = note?.Id || ""; editingRevision = note?.Updated || "";
    $("note-editor-title").textContent = note ? "编辑便签" : "新建便签";
    $("note-title").value = note?.Title || "";
    $("note-text").value = note?.Text || "";
    $("note-todo").checked = note?.IsTodo || false;
    $("note-length").textContent = `${$("note-text").value.length} / 2000`;
    $("note-error").textContent = "";
    $("note-editor").showModal(); syncInteraction();
    $("note-title").focus();
  }
  $("edge-notes-toggle").addEventListener("click", () => bridge?.postMessage({ command: "toggleEdgeNotes" }));
  for (const id of ["note-editor", "note-delete-dialog"]) $(id).addEventListener("close", syncInteraction);
  $("notes-nav").addEventListener("click", () => showNotes(true));
  $("all").addEventListener("click", () => showNotes(false));
  $("categories").addEventListener("click", () => showNotes(false), true);
  $("search").addEventListener("focus", () => showNotes(false));
  for (const id of ["new-note", "notes-empty-create"]) $(id).addEventListener("click", () => openEditor(null));
  for (const id of ["note-close", "note-cancel"]) $(id).addEventListener("click", () => $("note-editor").close());
  $("note-delete-cancel").addEventListener("click", () => $("note-delete-dialog").close());
  $("note-delete-confirm").addEventListener("click", () => request("deleteNote", { id: deleting, revision: deletingRevision }));
  for (const id of ["note-editor", "note-delete-dialog"]) $(id).addEventListener("cancel", (event) => { if (pending) event.preventDefault(); });
  $("note-text").addEventListener("input", () => { $("note-length").textContent = `${$("note-text").value.length} / 2000`; });
  $("note-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const title = $("note-title").value.trim(), text = $("note-text").value.trim();
    if (!title && !text) { $("note-error").textContent = "标题和内容至少写一项。"; $("note-text").focus(); return; }
    request("saveNote", { id: editing, revision: editingRevision, title, text, todo: String($("note-todo").checked) });
  });
  document.querySelectorAll("[data-note-filter]").forEach((item) => item.addEventListener("click", () => {
    filter = item.dataset.noteFilter; render();
  }));
  bridge?.addEventListener("message", (event) => {
    const data = event.data;
    if (["state", "notesState", "notesOpened"].includes(data?.type)) {
      if (edgeMode && data.type === "notesOpened") { filter = "all"; $("scroll").scrollTop = 0; $("notes-status").textContent = ""; }
      const changed = JSON.stringify(notes) !== JSON.stringify(data.notes || []) || loadError !== (data.notesError || "");
      notes = data.notes || []; loadError = data.notesError || "";
      if (data.type === "state") {
        $("edge-notes-toggle").textContent = "贴边呼出 · " + (data.edgeNotesEnabled === false ? "关" : "开");
        $("edge-notes-toggle").setAttribute("aria-pressed", String(data.edgeNotesEnabled !== false));
      }
      if (changed || data.type === "notesOpened") render();
      if (document.body.classList.contains("show-notes")) $("view-name").textContent = "便签";
    }
    if (data?.type !== "notesResult" || data.request !== pending?.request) return;
    const command = pending.command; pending = null;
    notes = data.notes || notes;
    const message = data.success ? ({ saveNote: "便签已保存", deleteNote: "便签已删除", toggleTodo: "待办状态已保存", copyNote: "已复制到剪贴板" })[command] : data.error || "操作未完成，请重试。";
    $("notes-status").textContent = message;
    $("note-error").textContent = data.success ? "" : message;
    if (data.success && command === "saveNote") $("note-editor").close();
    if (command === "deleteNote") $("note-delete-dialog").close();
    render();
  });
  if (edgeMode) {
    document.body.classList.add("edge-notes");
    showNotes(true);
    const heading = node("div", "notes-drawer-heading");
    heading.append(node("h1", "", "便签"), $("notes-summary"));
    document.querySelector(".topbar").prepend(heading);
    const toolbar = document.querySelector(".notes-toolbar");
    toolbar.append($("new-note"));
    $("new-note").textContent = "＋ 新建";
    $("theme-toggle").textContent = "◐";

    const collapse = document.querySelector('[data-command="hide"]');
    collapse.textContent = "收起"; collapse.title = "收起便签";
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && !event.defaultPrevented && !$("note-editor").open && !$("note-delete-dialog").open && !pending) {
        event.preventDefault(); bridge?.postMessage({ command: "hide" });
      }
    });
    const motion = matchMedia("(prefers-reduced-motion: reduce)");
    const syncMotion = () => bridge?.postMessage({ command: "notesMotion", value: String(motion.matches) });
    motion.addEventListener("change", syncMotion);
    window.addEventListener("DOMContentLoaded", () => { syncInteraction(); syncMotion(); }, { once: true });
  }
  render();
})();
