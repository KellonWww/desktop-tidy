(() => {
  "use strict";
  const $ = (id) => document.getElementById(id);
  const bridge = window.chrome?.webview;
  const edgeMode = new URLSearchParams(location.search).get("notesEdge") === "1";
  // Separate surface keys avoid one window overwriting another window's unsaved work.
  const storagePrefix = "desktop-tidy.notes." + (edgeMode ? "edge" : "workspace");
  let reading = "";
  let editingRevision = "", deletingRevision = "", lastInteraction = null;
  let notes = [], filter = "all", editing = "", deleting = "", pending = null, loadError = "";
  let query = "", showCompleted = false, editorBaseline = "", draftFault = false, quickFault = false;
  let drafts = readLocal("drafts", {}), quick = readLocal("quick", { text: "", todo: true });

  function readLocal(name, fallback) {
    try {
      const raw = localStorage.getItem(storagePrefix + "." + name);
      if (!raw) return fallback;
      const value = JSON.parse(raw);
      if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error("草稿格式无效");
      if (name === "quick" && (typeof value.text !== "string" || value.text.length > 2000 || typeof value.todo !== "boolean")) throw new Error("快速草稿无效");
      if (name === "drafts" && Object.entries(value).some(([key, item]) =>
        (key !== "new" && !/^[a-f0-9]{32}$/i.test(key)) || !item || typeof item.title !== "string" || item.title.length > 80 ||
        typeof item.text !== "string" || item.text.length > 2000 || typeof item.todo !== "boolean" || typeof item.revision !== "string")) throw new Error("编辑草稿无效");
      return value;
    } catch (error) {
      console.warn("Notes draft read failed", error);
      if (name === "drafts") draftFault = true; else quickFault = true;
      return fallback;
    }
  }
  function writeLocal(name, value) {
    if ((name === "drafts" && draftFault) || (name === "quick" && quickFault)) return false;
    try { localStorage.setItem(storagePrefix + "." + name, JSON.stringify(value)); return true; }
    catch (error) { console.warn("Notes draft write failed", error); return false; }
  }
  function node(tag, className, text) {
    const value = document.createElement(tag);
    if (className) value.className = className;
    if (text !== undefined) value.textContent = text;
    return value;
  }
  // Plain-text tokens only: never interpret a note as HTML or change its stored value.
  const syntaxTokens = /(`[^`\n]+`)|(https?:\/\/[^\s<>]+)|(\/\/[^\n]*|#[^\n]*)|("(?:\\.|[^"\\\n])*"|'(?:\\.|[^'\\\n])*')|(\*\*[^*\n]+\*\*)|\b(TODO|FIXME|NOTE|WARNING|SELECT|FROM|WHERE|JOIN|LEFT|RIGHT|INNER|OUTER|ON|AND|OR|NOT|NULL|INSERT|INTO|VALUES|UPDATE|SET|DELETE|ORDER|BY|GROUP|LIMIT|AS|DISTINCT|CREATE|TABLE|public|private|protected|class|interface|static|final|void|return|if|else|for|while|try|catch|throw|throws|new|const|let|var|function|async|await|import|export|true|false|null|undefined|def|in|is|None|True|False)\b|\b(\d+(?:\.\d+)?)\b/g;
  function highlight(target, text) {
    const fragment = document.createDocumentFragment();
    let offset = 0;
    for (const match of text.matchAll(syntaxTokens)) {
      fragment.append(document.createTextNode(text.slice(offset, match.index)));
      const kind = match[1] ? "code" : match[2] ? "link" : match[3] ? "comment" : match[4] ? "string" : match[5] ? "emphasis" : match[6] ? "keyword" : "number";
      fragment.append(node("span", "note-syntax-" + kind, match[0]));
      offset = match.index + match[0].length;
    }
    fragment.append(document.createTextNode(text.slice(offset)));
    target.replaceChildren(fragment);
  }
  function syncEditorHighlight() {
    const input = $("note-text"), backdrop = $("note-highlight");
    highlight(backdrop, input.value + "\n");
    backdrop.scrollTop = input.scrollTop;
    backdrop.scrollLeft = input.scrollLeft;
  }
  function applyFontSize(size) {
    if (!Number.isInteger(size) || size < 12 || size > 32) return;
    document.documentElement.style.setProperty("--note-font-size", size + "px");
    document.querySelectorAll(".note-font-size").forEach((select) => { select.value = String(size); });
    syncEditorHighlight();
  }
  document.querySelectorAll(".note-font-size").forEach((select) => {
    for (let size = 12; size <= 32; size++) {
      const option = node("option", "", size + " px"); option.value = String(size); select.append(option);
    }
    select.value = "16";
    select.addEventListener("change", () => {
      if (bridge) bridge.postMessage({ command: "noteFontSize", value: select.value });
      else applyFontSize(Number(select.value));
    });
  });
  function button(text, label, click) {
    const value = node("button", "note-action", text);
    value.type = "button"; value.setAttribute("aria-label", label); value.title = label;
    value.addEventListener("click", click); return value;
  }
  function iconButton(control, name) {
    const paths = {
      pin: "M9 3h6l-1 5 4 4v2H6v-2l4-4-1-5ZM12 14v7",
      trash: "M4 6h16M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7M14 10v7",
      grip: "M9 5v.01M15 5v.01M9 12v.01M15 12v.01M9 19v.01M15 19v.01"
    };
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", "0 0 24 24"); svg.setAttribute("aria-hidden", "true");
    const path = document.createElementNS(svg.namespaceURI, "path"); path.setAttribute("d", paths[name]);
    svg.append(path); control.replaceChildren(svg); control.classList.add("icon-button");
  }
  function dockLabel(status, side) {
    const names = { Left: "左侧", Right: "右侧", Top: "顶部", Bottom: "底部" };
    if (status.includes("松手吸附")) return status.split("（")[0].split(" · ")[0];
    if (status.includes("不在")) return "此屏幕不可停靠";
    return "已吸附" + (names[side] || "左侧");
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
  function refreshDrafts() {
    const count = Object.keys(drafts).length;
    $("note-drafts").hidden = !count && !draftFault;
    $("note-drafts-summary").textContent = draftFault ? "本地草稿读取失败，原草稿未覆盖。请先保存或复制当前内容。" : `${count} 份草稿`;
    $("note-restore").hidden = !count;
  }
  function editorValue() {
    return { title: $("note-title").value, text: $("note-text").value, todo: $("note-todo").checked, revision: editingRevision };
  }
  function persistEditor() {
    const value = editorValue(), key = editing || "new";
    const clean = JSON.stringify(value) === editorBaseline || (!editing && !value.title.trim() && !value.text.trim());
    const next = { ...drafts };
    if (clean) delete next[key]; else next[key] = { ...value, updated: Date.now() };
    const saved = writeLocal("drafts", next);
    if (saved) drafts = next;
    $("note-draft-status").textContent = !saved ? "草稿备份失败，请保存便签或复制内容；暂不关闭编辑。" : clean ? "" : "草稿已备份";
    refreshDrafts(); return clean || saved;
  }
  function clearEditorDraft() {
    const next = { ...drafts }; delete next[editing || "new"];
    if (writeLocal("drafts", next)) { drafts = next; refreshDrafts(); return true; }
    $("note-draft-status").textContent = "草稿清理失败，便签数据不受影响；下次可能仍提示恢复。";
    return false;
  }
  function persistQuick() {
    quick = { text: $("quick-note").value, todo: $("quick-todo").checked };
    const saved = writeLocal("quick", quick);
    $("quick-draft-status").textContent = !saved ? "草稿备份失败，请点击「记下来」保存；不要退出应用。" : quick.text.trim() ? "草稿已备份" : "";
    syncInteraction(); return saved;
  }
  function render() {
    const completed = notes.filter((note) => note.IsTodo && note.Completed).length;
    const todos = notes.filter((note) => note.IsTodo).length;
    $("notes-count").textContent = notes.length;
    $("notes-summary").textContent = `${notes.length} 条，${todos - completed} 项待办`;
    if (loadError) $("notes-status").textContent = loadError;
    document.querySelectorAll("[data-note-filter]").forEach((item) => item.setAttribute("aria-pressed", String(item.dataset.noteFilter === filter)));
    const visible = notes.filter((note) => (!query || `${note.Title}\n${note.Text}`.toLocaleLowerCase().includes(query)) && (filter === "all" ||
      (filter === "text" && !note.IsTodo) || (filter === "pending" && note.IsTodo && !note.Completed) || (filter === "done" && note.IsTodo && note.Completed)));
    const active = document.activeElement;
    const focusedId = active?.closest("[data-note-id]")?.dataset.noteId;
    const focusedAction = active?.dataset.noteAction;
    const grid = $("notes-grid"); grid.replaceChildren();
    let displayed = 0;
    for (const note of visible.slice().reverse().sort((a, b) => Number(Boolean(b.IsPinned)) - Number(Boolean(a.IsPinned)))) {
      const card = node("article", "note-card" + (note.Completed ? " completed" : "") + (note.IsPinned ? " pinned" : ""));
      card.dataset.noteId = note.Id;
      card.hidden = filter === "all" && !query && !showCompleted && note.IsTodo && note.Completed;
      if (!card.hidden) displayed++;
      const header = node("div", "note-card-header");
      header.append(node("span", "note-kind", (note.IsPinned ? "置顶 / " : "") + (note.IsTodo ? (note.Completed ? "已完成" : "待办") : "文本")));
      if (note.IsTodo) {
        const label = node("label", "note-check"), check = node("input");
        check.type = "checkbox"; check.checked = note.Completed; check.dataset.noteAction = "toggle";
        check.setAttribute("aria-label", (note.Completed ? "标记未完成：" : "标记完成：") + (note.Title || note.Text));
        check.addEventListener("change", () => request("toggleTodo", { id: note.Id, revision: note.Updated }));
        label.title = note.Completed ? "标记未完成" : "标记完成";
        label.append(check, node("span", "sr-only", note.Completed ? "已完成" : "完成")); header.append(label);
      }
      card.append(header);
      const preview = node("div", "note-preview");
      preview.tabIndex = 0; preview.setAttribute("role", "button"); preview.dataset.noteAction = "read";
      preview.setAttribute("aria-label", "放大阅读：" + (note.Title || note.Text.slice(0, 40)));
      preview.setAttribute("aria-haspopup", "dialog");
      if (note.Title) preview.append(node("h2", "note-title", note.Title));
      if (note.Text) {
        const body = node("p", "note-body"); highlight(body, note.Text); preview.append(body);
      }
      preview.title = "点击阅读全文";
      preview.addEventListener("click", () => { if (!window.getSelection()?.toString()) openReader(note); });
      preview.addEventListener("keydown", (event) => {
        if (event.key === "Enter" || event.key === " ") { event.preventDefault(); openReader(note); }
      });
      card.append(preview);
      const actions = node("div", "note-actions");
      const copy = button("复制", "复制便签文本", () => request("copyNote", { id: note.Id })); copy.dataset.noteAction = "copy";
      const edit = button("编辑", "编辑便签", () => openEditor(note)); edit.dataset.noteAction = "edit"; edit.classList.add("note-edit-primary");
      const pin = button(note.IsPinned ? "取消置顶" : "置顶", note.IsPinned ? "取消置顶便签" : "置顶便签", () => request("togglePin", { id: note.Id, revision: note.Updated }));
      iconButton(pin, "pin");
      pin.dataset.noteAction = "pin"; pin.setAttribute("aria-pressed", String(Boolean(note.IsPinned)));
      const remove = button("删除", "删除便签", () => { deleting = note.Id; deletingRevision = note.Updated; $("note-delete-dialog").showModal(); syncInteraction(); });
      iconButton(remove, "trash");
      remove.dataset.noteAction = "delete";
      const secondary = node("div", "note-secondary-actions"); secondary.append(copy, pin, remove);
      actions.append(edit, secondary); card.append(actions); grid.append(card);
    }
    $("notes-completed").hidden = filter !== "all" || Boolean(query) || completed === 0;
    $("notes-completed").textContent = `${showCompleted ? "收起" : "展开"}已完成（${completed}）`;
    $("notes-completed").setAttribute("aria-expanded", String(showCompleted));
    $("notes-empty").hidden = displayed > 0 || Boolean(loadError);
    $("notes-empty").querySelector("h2").textContent = query ? "没有匹配的便签" : notes.length ? "暂无便签" : "还没有便签";
    $("notes-empty-create").textContent = "＋ 新建便签";
    if ($("note-reader").open) refreshReader();
    refreshDrafts(); setBusy();
    if (focusedId && focusedAction) {
      const target = [...grid.querySelectorAll("[data-note-id]")].find((item) => item.dataset.noteId === focusedId && !item.hidden);
      target?.querySelector(`[data-note-action="${focusedAction}"]`)?.focus({ preventScroll: true });
      if (!target) document.querySelector(`[data-note-filter="${filter}"]`).focus({ preventScroll: true });
    }
  }
  function syncInteraction() {
    const active = Boolean(pending || $("note-reader").open || $("note-editor").open || $("note-delete-dialog").open || (document.activeElement?.closest("#quick-note-form") && $("quick-note").value.trim()));
    if (edgeMode && active !== lastInteraction) { lastInteraction = active; bridge?.postMessage({ command: "notesInteraction", value: String(active) }); }
  }
  function setBusy() {
    syncInteraction();
    document.querySelectorAll("#notes-view button:not([data-note-filter]), #notes-grid input, #quick-note-form textarea, #quick-note-form input, #note-form input, #note-form textarea, #note-form button, #note-delete-dialog button").forEach((item) => { item.disabled = Boolean(pending) || Boolean(loadError); });
    for (const id of ["note-close", "note-cancel", "note-discard", "note-delete-cancel", "note-copy-draft"]) $(id).disabled = Boolean(pending);
    $("note-save").textContent = pending?.command === "saveNote" ? "正在保存…" : "保存";
  }
  function request(command, extra, source = "editor") {
    if (pending || (loadError && command !== "copyDraft")) return;
    if (!bridge) { $("notes-status").textContent = "便签需要在桌面应用中使用。"; return; }
    pending = { command, request: crypto.randomUUID(), source };
    $("note-error").textContent = ""; $("notes-status").textContent = "正在处理…"; setBusy();
    bridge.postMessage({ command, request: pending.request, ...extra });
  }
  function refreshReader() {
    const note = notes.find((item) => item.Id === reading);
    $("note-reader-title").textContent = note?.Title || (note ? "便签全文" : "便签已删除");
    highlight($("note-reader-body"), note?.Text || (note ? "这条便签只有标题。" : "这条便签已在另一处删除。"));
    $("note-reader-edit").disabled = !note || Boolean(pending) || Boolean(loadError);
  }
  function openReader(note) {
    if (pending || loadError || $("note-reader").open) return;
    reading = note.Id; refreshReader(); $("note-reader").showModal(); syncInteraction();
    $("note-reader-body").scrollTop = 0; $("note-reader-body").focus();
  }
  function openEditor(note, restoredKey) {
    if (pending || loadError || $("note-editor").open) return;
    editing = restoredKey === undefined ? note?.Id || "" : restoredKey === "new" ? "" : restoredKey;
    editingRevision = note?.Updated || "";
    $("note-editor-title").textContent = editing ? "编辑便签" : "新建便签";
    $("note-title").value = note?.Title || ""; $("note-text").value = note?.Text || ""; $("note-todo").checked = note?.IsTodo || false;
    editorBaseline = JSON.stringify(editorValue());
    const draft = drafts[editing || "new"];
    if (draft) {
      editingRevision = draft.revision; $("note-title").value = draft.title; $("note-text").value = draft.text; $("note-todo").checked = draft.todo;
    }
    $("note-length").textContent = `${$("note-text").value.length} / 2000`;
    $("note-error").textContent = ""; $("note-draft-status").textContent = draft ? "已恢复本机草稿，尚未保存便签。" : "";
    $("note-conflict").hidden = true;
    $("note-editor").showModal(); syncEditorHighlight(); syncInteraction(); $("note-text").focus();
    if (draft && editing && (!note || note.Updated !== editingRevision)) showConflict();
  }
  function showConflict() {
    const latest = notes.find((note) => note.Id === editing);
    $("note-conflict").hidden = false;
    $("note-latest").textContent = latest ? [latest.Title, latest.Text].filter(Boolean).join("\n\n") : "这条便签已被删除。你的草稿仍可复制或另存。";
  }
  function closeEditor() { if (!pending && persistEditor()) $("note-editor").close(); }
  function submitEditor(asCopy = false) {
    const title = $("note-title").value.trim(), text = $("note-text").value.trim();
    if (!title && !text) { $("note-error").textContent = "标题和内容至少写一项。"; $("note-text").focus(); return; }
    persistEditor();
    request("saveNote", { id: asCopy ? "" : editing, revision: asCopy ? "" : editingRevision, title, text, todo: String($("note-todo").checked) });
  }
  $("quick-note").value = quick.text; $("quick-todo").checked = quick.todo;
  $("quick-draft-status").textContent = quickFault ? "本地快速草稿读取失败，原草稿未覆盖。" : quick.text ? "已恢复快速记录草稿，点击「记下来」保存。" : "";
  $("quick-note").addEventListener("input", persistQuick); $("quick-todo").addEventListener("change", persistQuick);
  // Collapse after pointer activation, not on blur: moving the list mid-click causes mis-hits.
  let pointerActive = false;
  const settleCapture = () => {
    pointerActive = false;
    $("quick-note-form").classList.toggle("capture-expanded", $("quick-note-form").contains(document.activeElement));
  };
  document.addEventListener("pointerdown", () => { pointerActive = true; }, true);
  document.addEventListener("pointerup", () => setTimeout(settleCapture, 0), true);
  document.addEventListener("pointercancel", () => setTimeout(settleCapture, 0), true);
  $("quick-note-form").addEventListener("focusin", () => { if (!pointerActive) $("quick-note-form").classList.add("capture-expanded"); syncInteraction(); });
  $("quick-note-form").addEventListener("focusout", () => { if (!pointerActive) setTimeout(settleCapture, 0); });
  $("quick-note-form").addEventListener("focusout", () => queueMicrotask(syncInteraction));
  $("quick-note").addEventListener("keydown", (event) => {
    if (event.key !== "Enter" || event.shiftKey || event.isComposing || event.keyCode === 229) return;
    event.preventDefault(); $("quick-note-form").requestSubmit();
  });
  $("quick-note-form").addEventListener("submit", (event) => {
    event.preventDefault(); const text = $("quick-note").value.trim();
    if (!text) { $("quick-draft-status").textContent = "先写下一点内容。"; $("quick-note").focus(); return; }
    request("saveNote", { id: "", revision: "", title: "", text, todo: String($("quick-todo").checked) }, "quick");
  });
  $("notes-search").addEventListener("input", () => { query = $("notes-search").value.trim().toLocaleLowerCase(); render(); });
  $("notes-completed").addEventListener("click", () => { showCompleted = !showCompleted; render(); });
  $("note-restore").addEventListener("click", () => {
    const key = Object.keys(drafts).sort((a, b) => (drafts[b].updated || 0) - (drafts[a].updated || 0))[0];
    if (key) openEditor(notes.find((note) => note.Id === key), key);
  });
  $("edge-notes-toggle").addEventListener("click", () => bridge?.postMessage({ command: "toggleEdgeNotes" }));
  for (const id of ["edge-delay", "edge-screen"]) $(id).addEventListener("change", () => bridge?.postMessage({ command: "configureEdgeNotes", delay: Number($("edge-delay").value), screen: $("edge-screen").value }));
  $("edge-pause").addEventListener("click", () => bridge?.postMessage({ command: "pauseEdgeNotes" }));
  for (const id of ["note-reader", "note-editor", "note-delete-dialog"]) $(id).addEventListener("close", syncInteraction);
  $("notes-nav").addEventListener("click", () => showNotes(true));
  $("all").addEventListener("click", () => showNotes(false));
  $("categories").addEventListener("click", () => showNotes(false), true);
  $("search").addEventListener("focus", () => showNotes(false));
  for (const id of ["new-note", "notes-empty-create"]) $(id).addEventListener("click", () => openEditor(null));
  for (const id of ["note-close", "note-cancel"]) $(id).addEventListener("click", closeEditor);
  $("note-discard").addEventListener("click", () => { if (clearEditorDraft()) $("note-editor").close(); });
  $("note-copy-draft").addEventListener("click", () => request("copyDraft", { text: [$("note-title").value, $("note-text").value].filter(Boolean).join("\n\n") }));
  $("note-save-copy").addEventListener("click", () => submitEditor(true));
  $("note-reader-close").addEventListener("click", () => $("note-reader").close());
  $("note-reader-edit").addEventListener("click", () => {
    const note = notes.find((item) => item.Id === reading);
    if (!note || pending || loadError) return;
    $("note-reader").close(); openEditor(note);
  });
  $("note-delete-cancel").addEventListener("click", () => $("note-delete-dialog").close());
  $("note-delete-confirm").addEventListener("click", () => request("deleteNote", { id: deleting, revision: deletingRevision }));
  $("note-editor").addEventListener("cancel", (event) => { if (pending || !persistEditor()) event.preventDefault(); });
  $("note-delete-dialog").addEventListener("cancel", (event) => { if (pending) event.preventDefault(); });
  $("note-text").addEventListener("input", syncEditorHighlight);
  $("note-text").addEventListener("scroll", syncEditorHighlight);
  new ResizeObserver(syncEditorHighlight).observe($("note-text"));
  for (const id of ["note-title", "note-text", "note-todo"]) $(id).addEventListener("input", () => { $("note-length").textContent = `${$("note-text").value.length} / 2000`; persistEditor(); });
  $("note-form").addEventListener("submit", (event) => { event.preventDefault(); submitEditor(); });
  $("note-form").addEventListener("keydown", (event) => {
    if (event.ctrlKey && event.key === "Enter" && !event.isComposing) { event.preventDefault(); submitEditor(); }
  });
  document.querySelectorAll("[data-note-filter]").forEach((item) => item.addEventListener("click", () => { filter = item.dataset.noteFilter; render(); }));
  bridge?.addEventListener("message", (event) => {
    const data = event.data;
    if (["state", "notesState", "notesOpened"].includes(data?.type)) {
      if (data.noteFontSize !== undefined) applyFontSize(data.noteFontSize);
      if (edgeMode && data.type === "notesOpened") { filter = "all"; query = ""; $("notes-search").value = ""; $("scroll").scrollTop = 0; $("notes-status").textContent = ""; }
      const changed = JSON.stringify(notes) !== JSON.stringify(data.notes || []) || loadError !== (data.notesError || "");
      notes = data.notes || []; loadError = data.notesError || "";
      if (data.type === "state") {
        $("edge-notes-toggle").textContent = data.edgeNotesEnabled === false ? "已关闭" : "已开启";
        $("edge-notes-toggle").setAttribute("aria-pressed", String(data.edgeNotesEnabled !== false));
        $("edge-delay").value = String(data.edgeNotesDelay || 600);
        const dockStatus = data.edgeNotesDockStatus || "已吸附左侧 · 拖动顶部可换边";
        $("edge-dock-location").textContent = dockLabel(dockStatus, data.edgeNotesDockSide);
        $("edge-dock-location").title = dockStatus;
        if ($("note-dock-location")) { $("note-dock-location").textContent = dockLabel(dockStatus, data.edgeNotesDockSide); $("note-dock-location").title = dockStatus; }
        const screens = [{ id: "all", name: "全部屏幕" }, { id: "primary", name: "仅主屏幕" }, ...(data.notesScreens || [])];
        if (!screens.some((screen) => screen.id === data.edgeNotesScreen) && data.edgeNotesScreen) screens.push({ id: data.edgeNotesScreen, name: "已断开屏幕（请选择）" });
        const signature = JSON.stringify(screens);
        if ($("edge-screen").dataset.options !== signature) {
          $("edge-screen").replaceChildren(...screens.map((screen) => { const option = node("option", "", screen.name); option.value = screen.id; return option; }));
          $("edge-screen").dataset.options = signature;
        }
        $("edge-screen").value = data.edgeNotesScreen || "all";
        $("edge-pause").textContent = data.edgeNotesPaused ? "恢复悬停呼出" : "暂停悬停呼出";
        $("edge-pause").setAttribute("aria-pressed", String(Boolean(data.edgeNotesPaused)));
      }
      if (changed || data.type === "notesOpened") render();
      if ($("note-editor").open && editing && notes.find((note) => note.Id === editing)?.Updated !== editingRevision) showConflict();
      if (document.body.classList.contains("show-notes")) $("view-name").textContent = "便签";
    }
    if (data?.type !== "notesResult" || data.request !== pending?.request) return;
    const operation = pending; pending = null; notes = data.notes || notes;
    const message = data.success ? ({ saveNote: "便签已保存", deleteNote: "便签已删除", toggleTodo: "待办状态已保存", togglePin: "置顶状态已保存", copyDraft: "草稿已复制到剪贴板", copyNote: "已复制到剪贴板" })[operation.command] : data.error || "操作未完成，请重试。";
    $("notes-status").textContent = message; $("note-error").textContent = data.success ? "" : message;
    if (data.success && operation.command === "saveNote") {
      if (operation.source === "quick") { $("quick-note").value = ""; persistQuick(); }
      else { clearEditorDraft(); $("note-editor").close(); }
    }
    if (!data.success && operation.source === "editor" && editing && notes.find((note) => note.Id === editing)?.Updated !== editingRevision) showConflict();
    if (operation.command === "deleteNote") $("note-delete-dialog").close();
    render();
    if (data.success && operation.command === "saveNote" && operation.source === "quick") $("quick-note").focus();
  });
  document.addEventListener("pointerdown", (event) => {
    const settings = $("notes-edge-settings");
    if (settings.open && !settings.contains(event.target)) settings.open = false;
  });
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && $("notes-edge-settings").open) {
      $("notes-edge-settings").open = false; $("notes-edge-settings").querySelector("summary").focus();
      event.preventDefault();
    }
  });
  if (edgeMode) {
    document.body.classList.add("edge-notes"); showNotes(true);
    const heading = node("div", "notes-drawer-heading"), headingRow = node("div", "notes-drawer-title-row");
    const drag = button("", "拖动便签选择吸附边", () => {}); drag.id = "note-dock-drag";
    iconButton(drag, "grip");
    drag.title = "按住拖动，松手吸附最近的上、下、左、右边缘；Esc 取消";
    headingRow.append(node("h1", "", "便签"), drag);
    const location = node("p", "", "已吸附左侧"); location.id = "note-dock-location";
    heading.append(headingRow, location); document.querySelector(".topbar").prepend(heading);
    heading.addEventListener("pointerdown", (event) => {
      if (event.button !== 0 || pending || document.querySelector("dialog[open]")) return;
      event.preventDefault(); document.activeElement?.blur(); bridge?.postMessage({ command: "move" });
    });
    document.querySelector(".notes-toolbar").append($("new-note")); $("new-note").textContent = "＋ 新建";
    const collapse = document.querySelector('[data-command="hide"]'); collapse.title = "收起便签"; collapse.setAttribute("aria-label", "收起便签");
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && !event.defaultPrevented && !$("note-reader").open && !$("note-editor").open && !$("note-delete-dialog").open && !pending) { event.preventDefault(); bridge?.postMessage({ command: "hide" }); }
    });
    const motion = matchMedia("(prefers-reduced-motion: reduce)");
    const syncMotion = () => bridge?.postMessage({ command: "notesMotion", value: String(motion.matches) });
    motion.addEventListener("change", syncMotion);
    window.addEventListener("DOMContentLoaded", () => { syncInteraction(); syncMotion(); }, { once: true });
  }
  render();
})();
