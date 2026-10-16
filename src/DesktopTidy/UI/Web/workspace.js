(() => {
  "use strict";
  const $ = (id) => document.getElementById(id);
  const bridge = window.chrome?.webview;
  const motion = window.matchMedia("(prefers-reduced-motion: reduce)");
  const animate = () => !motion.matches && Boolean(window.gsap);
  const send = (command, extra = {}) =>
    bridge?.postMessage({ command, ...extra });
  const nodes = new Map();
  let state = null,
    selected = "",
    dragged = null,
    assignItem = null,
    first = true;
  let searchPending = null;
  const folderSvg =
    '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 7V5a1 1 0 0 1 1-1h5l2 3h9a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V7Z"/></svg>';
  function element(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }
  function action(text, command, extra, className) {
    const button = element("button", className, text);
    button.type = "button";
    button.addEventListener("click", (event) => {
      event.stopPropagation();
      button.closest("details")?.removeAttribute("open");
      send(command, extra);
    });
    return button;
  }
  function chooseCategory(item) {
    assignItem = item;
    $("assign-name").textContent = item.name;
    $("assign-options").replaceChildren();
    for (const group of state.groups) {
      const button = action(group.title, "assign", {
        item: item.id,
        category: String(group.id),
      });
      button.addEventListener("click", () => $("assign-dialog").close());
      $("assign-options").append(button);
    }
    if (!$("assign-dialog").open) $("assign-dialog").showModal();
  }
  function makeItem(item) {
    const button = element("button", "item");
    button.dataset.item = item.id;
    button.type = "button";
    button.draggable = true;
    const image = element("img");
    image.alt = "";
    image.draggable = false;
    const name = element("span", "item-name");
    button.append(image, name);
    button.addEventListener("click", () => {
      selected = item.id;
      document.querySelectorAll(".item").forEach((node) => {
        const isSelected = node === button;
        node.classList.toggle("selected", isSelected);
        node.setAttribute("aria-pressed", String(isSelected));
      });
    });
    button.addEventListener("contextmenu", (event) => event.preventDefault());
    button.addEventListener("dblclick", () => send("open", { item: item.id }));
    button.addEventListener("keydown", (event) => {
      if (event.key === "Enter") {
        event.preventDefault();
        send("open", { item: item.id });
      }
      if (event.ctrlKey && event.key.toLowerCase() === "m") {
        event.preventDefault();
        chooseCategory(button._item);
      }
      if (
        ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)
      ) {
        event.preventDefault();
        const items = [
          ...button.closest(".card-items").querySelectorAll(".item"),
        ];
        const columns = Math.max(
          1,
          Math.round(
            button.parentElement.clientWidth /
              Math.max(button.offsetWidth + 3, 1),
          ),
        );
        const step = {
          ArrowLeft: -1,
          ArrowRight: 1,
          ArrowUp: -columns,
          ArrowDown: columns,
        }[event.key];
        items[
          Math.max(0, Math.min(items.length - 1, items.indexOf(button) + step))
        ]?.focus();
      }
    });
    button.addEventListener("dragstart", (event) => {
      dragged = { type: "item", id: item.id };
      event.dataTransfer.setData("text/plain", "DesktopTidy item");
      event.dataTransfer.effectAllowed = "move";
      button.classList.add("dragging");
    });
    button.addEventListener("dragend", clearDrag);
    return button;
  }
  function clearDrag() {
    dragged = null;
    document
      .querySelectorAll(".drag-over,.dragging,.reorder-over,.dragging-group")
      .forEach((node) =>
        node.classList.remove(
          "drag-over",
          "dragging",
          "reorder-over",
          "dragging-group",
        ),
      );
  }
  function gridMetrics() {
    const grid = $("grid");
    const style = getComputedStyle(grid);
    const columns = style.gridTemplateColumns
      .split(" ")
      .filter((value) => value && value !== "none")
      .map((value) => Number.parseFloat(value));
    const columnGap = Number.parseFloat(style.columnGap) || 12;
    const rowGap = Number.parseFloat(style.rowGap) || 12;
    return {
      columns: Math.max(1, columns.length),
      column: columns[0] || 72,
      columnGap,
      row: Number.parseFloat(style.gridAutoRows) || 32,
      rowGap,
    };
  }
  function applyCardSize(card, group, requestedWidth, requestedHeight) {
    const metrics = gridMetrics();
    const width = Math.max(255, requestedWidth || group.width || 280);
    const height = Math.max(220, requestedHeight || group.height || 260);
    const columnSpan = Math.min(
      metrics.columns,
      Math.max(
        Math.min(3, metrics.columns),
        Math.round((width + metrics.columnGap) / (metrics.column + metrics.columnGap)),
      ),
    );
    const rowSpan = card.classList.contains("collapsed")
      ? 2
      : Math.max(
          5,
          Math.min(
            40,
            Math.round((height + metrics.rowGap) / (metrics.row + metrics.rowGap)),
          ),
        );
    card.style.gridColumnEnd = "span " + columnSpan;
    card.style.gridRowEnd = "span " + rowSpan;
  }
  function beginCardResize(event, card) {
    if (event.button !== 0 || !card._group || card._group.collapsed) return;
    event.preventDefault();
    event.stopPropagation();
    const handle = event.currentTarget;
    const bounds = card.getBoundingClientRect();
    const start = { x: event.clientX, y: event.clientY, width: bounds.width, height: bounds.height };
    card.classList.add("resizing-card");
    document.documentElement.classList.add("dragging-global");
    handle.setPointerCapture(event.pointerId);
    const move = (next) => {
      applyCardSize(card, card._group, start.width + next.clientX - start.x, start.height + next.clientY - start.y);
    };
    const finish = (next, save) => {
      handle.removeEventListener("pointermove", move);
      handle.removeEventListener("pointerup", complete);
      handle.removeEventListener("pointercancel", cancel);
      if (handle.hasPointerCapture(next.pointerId)) handle.releasePointerCapture(next.pointerId);
      card.classList.remove("resizing-card");
      document.documentElement.classList.remove("dragging-global");
      if (!save) { applyCardSize(card, card._group); return; }
      const snapped = card.getBoundingClientRect();
      card._group.width = Math.round(snapped.width);
      card._group.height = Math.round(snapped.height);
      send("resizeCategory", {
        category: String(card._group.id),
        width: String(card._group.width),
        height: String(card._group.height),
      });
    };
    const complete = (next) => finish(next, true);
    const cancel = (next) => finish(next, false);
    handle.addEventListener("pointermove", move);
    handle.addEventListener("pointerup", complete);
    handle.addEventListener("pointercancel", cancel);
  }
  function refreshCardSizes() {
    nodes.forEach((card) => {
      if (!card.hidden && card._group) applyCardSize(card, card._group);
    });
  }
  function dropTarget(node, id) {
    node.addEventListener("dragover", (event) => {
      if (!dragged || (dragged.type === "group" && String(id) === dragged.id))
        return;
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
      node.classList.add(
        dragged.type === "group" ? "reorder-over" : "drag-over",
      );
    });
    node.addEventListener("dragleave", (event) => {
      if (!node.contains(event.relatedTarget))
        node.classList.remove("drag-over", "reorder-over");
    });
    node.addEventListener("drop", (event) => {
      if (!dragged) return;
      event.preventDefault();
      event.stopPropagation();
      if (dragged.type === "item")
        send("assign", { item: dragged.id, category: String(id) });
      else send("reorder", { category: dragged.id, before: String(id) });
      clearDrag();
    });
  }
  function makeCard(group) {
    const card = element("section", "category-card");
    card.id = `category-${group.id}`;
    card.dataset.category = String(group.id);
    const header = element("header", "card-header");
    header.draggable = true;
    header.title = "拖动标题调整分类顺序";
    header.addEventListener("dragstart", (event) => {
      dragged = { type: "group", id: String(group.id) };
      event.dataTransfer.setData("text/plain", "DesktopTidy category");
      event.dataTransfer.effectAllowed = "move";
      card.classList.add("dragging-group");
    });
    header.addEventListener("dragend", clearDrag);
    const mark = element("span", "category-mark");
    mark.innerHTML = folderSvg;
    const title = element("h2");
    title.id = `title-${group.id}`;
    card.setAttribute("aria-labelledby", title.id);
    const count = element("span", "card-count");
    const actions = element("div", "card-actions");
    const collapse = action("−", "collapse", { category: String(group.id) });
    const details = element("details", "card-menu");
    const summary = element("summary", "", "⋯");
    summary.setAttribute("aria-label", "分类操作");
    const menu = element("div");
    menu.append(
      action("重命名分类", "rename", { category: String(group.id) }),
      action("删除分类", "delete", { category: String(group.id) }, "danger"),
      element("p", "", "只删分类，不删原文件"),
    );
    details.append(summary, menu);
    actions.append(collapse, details);
    header.append(mark, title, count, actions);
    const contents = element("div", "card-items");
    const footer = element("div", "card-footer");
    const resize = element("button", "card-resize", "◢");
    resize.type = "button";
    resize.title = "拖动调整分类框大小，松手后自动对齐";
    resize.setAttribute("aria-label", "调整分类框大小");
    resize.addEventListener("pointerdown", (event) => beginCardResize(event, card));
    footer.append(element("span"), element("span", "", "拖入归类"), resize);
    card.append(header, contents, footer);
    dropTarget(card, group.id);
    card._parts = {
      title,
      count,
      collapse,
      contents,
      footer,
      items: new Map(),
      signature: "",
    };
    return card;
  }
  function updateCard(card, group, query) {
    const part = card._parts;
    const source = group.items.filter(
      (item) => !query || item.name.toLocaleLowerCase().includes(query),
    );
    const sectionOrder = ["音乐", "影视", "游戏", "社交", "其他"];
    if (group.id === 24 || group.sortByName)
      source.sort((a, b) => {
        if (group.id === 24) {
          const rank = (item) =>
            Math.max(0, sectionOrder.indexOf(item.section || "其他"));
          const diff = rank(a) - rank(b);
          if (diff) return diff;
        }
        return a.name.localeCompare(b.name, "zh-CN");
      });
    part.title.textContent = group.title;
    part.count.textContent = String(source.length).padStart(2, "0");
    const shouldCollapse = group.collapsed && !query;
    card._group = group;
    card.classList.toggle("collapsed", shouldCollapse);
    applyCardSize(card, group);
    part.collapse.textContent = group.collapsed ? "+" : "−";
    part.collapse.classList.toggle("is-expanded", !shouldCollapse);
    part.contents.setAttribute("aria-hidden", String(shouldCollapse));
    part.contents.inert = shouldCollapse;
    part.footer.setAttribute("aria-hidden", String(shouldCollapse));
    part.collapse.title = group.collapsed ? "展开分类" : "折叠分类";
    part.collapse.setAttribute(
      "aria-label",
      `${part.collapse.title}：${group.title}`,
    );
    part.collapse.setAttribute(
      "aria-expanded",
      String(!group.collapsed || Boolean(query)),
    );
    part.footer.firstChild.textContent = query
      ? `${source.length} 个匹配`
      : `${group.items.length} 个图标`;
    const signature = JSON.stringify(
      source.map((item) => [item.id, item.name, item.icon, item.section]),
    );
    if (signature === part.signature) return;
    part.signature = signature;
    const keep = new Set(source.map((item) => item.id));
    part.items.forEach((node, id) => {
      if (!keep.has(id)) {
        node.remove();
        part.items.delete(id);
      }
    });
    part.contents
      .querySelectorAll(".section-label,.empty-card")
      .forEach((node) => node.remove());
    let section = null;
    for (const item of source) {
      if (group.id === 24 && (item.section || "其他") !== section) {
        section = item.section || "其他";
        part.contents.append(element("div", "section-label", section));
      }
      let node = part.items.get(item.id);
      if (!node) {
        node = makeItem(item);
        part.items.set(item.id, node);
      }
      node._item = item;
      node.title = `${item.name}\n双击打开 · 拖动归类 · Ctrl+M 归类`;
      node.setAttribute("aria-label", `${item.name}，Enter 打开，拖动归类，Ctrl+M 归类`);
      node.firstChild.src = item.icon;
      node.lastChild.textContent = item.name;
      node.classList.toggle("selected", selected === item.id);
      part.contents.append(node);
    }
    if (!source.length)
      part.contents.append(
        element("p", "empty-card", "把图标拖到这里，开始整理。"),
      );
  }
  function render(next) {
    state = next;
    document.documentElement.classList.toggle("dark", state.dark);
    const query = (state.search || "").trim().toLocaleLowerCase();
    if (searchPending === null || state.search === searchPending) {
      $("search").value = state.search;
      searchPending = null;
    }
    $("total").textContent = state.total;
    $("heading").replaceChildren(
      document.createTextNode(query ? "搜索结果" : "我的桌面"),
      element("span", "", "."),
    );
    $("view-name").textContent = query ? "搜索结果" : "全部图标";
    const matches = state.groups.reduce(
      (sum, group) =>
        sum +
        group.items.filter(
          (item) => !query || item.name.toLocaleLowerCase().includes(query),
        ).length,
      0,
    );
    $("summary").textContent = query
      ? `${matches} / ${state.total} 个图标匹配 · 搜索不会改变分类`
      : `${state.groups.length} 个分类，${state.total} 个图标。每样东西，都有自己的位置。`;
    $("updated").textContent = state.updated;
    $("sort").textContent = state.nextDescending ? "数量 ↓" : "数量 ↑";
    $("sort").title = "按全部图标数量排序；一次性操作，可撤销";
    $("collapse").textContent = state.allCollapsed ? "全部展开" : "全部折叠";
    $("refresh").disabled = state.refreshing;
    $("refresh").lastElementChild.textContent = state.refreshing
      ? "正在刷新…"
      : "刷新桌面";
    $("hide-empty").checked = state.hideEmpty;
    $("startup").checked = state.hideOnStartup;
    $("dark-theme").checked = state.dark;
    $("dark-theme").disabled = state.highContrast;
    $("theme-toggle").disabled = state.highContrast;
    $("theme-toggle").title = state.highContrast
      ? "系统高对比度已接管主题"
      : state.dark ? "切换到浅色主题" : "切换到深色主题";
    $("theme-toggle").setAttribute("aria-label", $("theme-toggle").title);
    $("system-theme").disabled = state.highContrast || state.followsSystemTheme;
    $("theme-note").textContent = state.highContrast
      ? "系统高对比度已启用，主题由 Windows 接管。"
      : state.followsSystemTheme
        ? "当前跟随 Windows 应用模式；切换后会记住你的选择。"
        : "当前为手动主题；可随时恢复跟随 Windows。";
    if (document.activeElement !== $("opacity"))
      $("opacity").value = state.opacity;
    $("opacity-value").textContent = `${$("opacity").value}%`;
    $("desktop").textContent =
      state.desktopVisible === true ? "隐藏原生桌面图标" : "显示原生桌面图标";
    if ($("feedback").textContent !== state.feedback) {
      $("feedback").textContent = state.feedback;
      if (animate())
        gsap.fromTo(
          $("feedback"),
          { opacity: 0.35, y: 3 },
          { opacity: 1, y: 0, duration: 0.25, overwrite: true },
        );
    }
    const scrollTop = $("scroll").scrollTop;
    const focused = document.activeElement;
    const oldRects = new Map(
      [...nodes].map(([id, node]) => [id, node.getBoundingClientRect()]),
    );
    const existing = new Set(state.groups.map((group) => group.id));
    nodes.forEach((node, id) => {
      if (!existing.has(id)) {
        if (window.ScrollTrigger)
          ScrollTrigger.getAll()
            .filter((trigger) => trigger.trigger === node)
            .forEach((trigger) => trigger.kill());
        node.remove();
        nodes.delete(id);
      }
    });
    const added = [];
    const sideSignature = JSON.stringify(
      state.groups.map((group) => [group.id, group.title, group.items.length]),
    );
    if ($("categories").dataset.signature !== sideSignature) {
      $("categories").dataset.signature = sideSignature;
      const sideScroll = $("categories").scrollTop;
      $("categories").replaceChildren();
      for (const group of state.groups) {
        const link = element("button", "category-link");
        link.dataset.category = group.id;
        link.append(
          element("span", "dot"),
          element("span", "name", group.title),
          element("span", "count", group.items.length),
        );
        link.title = group.title;
        dropTarget(link, group.id);
        link.addEventListener("click", () => {
          if (state.search) {
            searchPending = "";
            send("search", { value: "" });
          }
          requestAnimationFrame(() => {
            const card = nodes.get(group.id);
            if (!card || card.hidden) return;
            card.scrollIntoView({
              behavior: motion.matches ? "instant" : "smooth",
              block: "start",
            });
            document
              .querySelectorAll(".category-link.current")
              .forEach((node) => node.classList.remove("current"));
            link.classList.add("current");
            card.classList.add("scroll-highlight");
            setTimeout(() => card.classList.remove("scroll-highlight"), 1000);
          });
        });
        $("categories").append(link);
      }
      $("categories").scrollTop = sideScroll;
    }
    let visibleCount = 0;
    const revealed = [];
    for (const group of state.groups) {
      let card = nodes.get(group.id);
      const existed = Boolean(card);
      if (!card) {
        card = makeCard(group);
        nodes.set(group.id, card);
        added.push(card);
      }
      const wasHidden = card.hidden;
      updateCard(card, group, query);
      const matches = group.items.some((item) =>
        item.name.toLocaleLowerCase().includes(query),
      );
      card.hidden = query ? !matches : state.hideEmpty && !group.items.length;
      if (existed && wasHidden && !card.hidden) revealed.push(card);
      if (!card.hidden) visibleCount++;
      // Do not detach unchanged nodes: focus and native drag state must survive status updates.
      const position = state.groups.indexOf(group);
      if ($("grid").children[position] !== card)
        $("grid").insertBefore(card, $("grid").children[position] || null);
    }
    $("empty").hidden = visibleCount > 0;
    $("empty").querySelector("h2").textContent = query
      ? "没有找到匹配的图标"
      : "暂时没有可显示的分类";
    $("empty").querySelector("p").textContent = query
      ? "换个名称试试，或清空搜索查看全部。"
      : "刷新桌面，或在设置中关闭“隐藏空分类”。";
    $("clear-search").hidden = !query;
    $("scroll").scrollTop = scrollTop;
    if (
      focused?.isConnected &&
      document.activeElement !== focused &&
      focused.classList.contains("item")
    )
      focused.focus({ preventScroll: true });
    if (animate()) {
      for (const card of revealed) {
        gsap.fromTo(
          card,
          { opacity: 0.7, y: 8, scale: 0.99 },
          {
            opacity: 1,
            y: 0,
            scale: 1,
            duration: 0.22,
            ease: "power2.out",
            overwrite: true,
          },
        );
      }
      const revealedSet = new Set(revealed);
      for (const [id, card] of nodes) {
        const old = oldRects.get(id);
        if (!old || card.hidden || !old.width || revealedSet.has(card)) continue;
        const rect = card.getBoundingClientRect();
        if (Math.abs(rect.x - old.x) + Math.abs(rect.y - old.y) > 2)
          gsap.fromTo(
            card,
            { x: old.x - rect.x, y: old.y - rect.y },
            { x: 0, y: 0, duration: 0.36, ease: "power2.out", overwrite: true },
          );
      }
      for (const card of added.filter((node) => !node.hidden)) {
        gsap.fromTo(
          card,
          { opacity: 0.7, y: 14 },
          {
            opacity: 1,
            y: 0,
            duration: 0.4,
            scrollTrigger: window.ScrollTrigger
              ? {
                  trigger: card,
                  scroller: $("scroll"),
                  start: "top 98%",
                  once: true,
                }
              : undefined,
          },
        );
      }
      if (first)
        gsap.fromTo(
          ".overview",
          { y: 10, opacity: 0.6 },
          { y: 0, opacity: 1, duration: 0.45 },
        );
      if (window.ScrollTrigger) ScrollTrigger.refresh();
    }
    first = false;
  }
  document.querySelectorAll("[data-command]").forEach((button) =>
    button.addEventListener("click", () => {
      if (button.dataset.command === "native") $("settings-dialog").close();
      send(button.dataset.command);
    }),
  );
  $("search").addEventListener("input", () => {
    searchPending = $("search").value;
    send("search", { value: searchPending });
    $("scroll").scrollTop = 0;
  });
  const clearSearch = () => {
    searchPending = "";
    $("search").value = "";
    send("search", { value: "" });
    $("search").focus();
  };
  $("all").addEventListener("click", () => {
    clearSearch();
    $("scroll").scrollTop = 0;
  });
  $("clear-search").addEventListener("click", clearSearch);
  $("theme-toggle").addEventListener("click", () => {
    if (state && !state.highContrast)
      send("setTheme", { value: String(!state.dark) });
  });
  $("settings").addEventListener("click", () =>
    $("settings-dialog").showModal(),
  );
  $("close-settings").addEventListener("click", () =>
    $("settings-dialog").close(),
  );
  $("close-assign").addEventListener("click", () => {
    assignItem = null;
    $("assign-dialog").close();
  });
  $("hide-empty").addEventListener("change", () => send("hideEmpty"));
  $("dark-theme").addEventListener("change", (event) =>
    send("setTheme", { value: String(event.target.checked) }),
  );
  $("startup").addEventListener("change", (event) =>
    send("startup", { value: String(event.target.checked) }),
  );
  $("opacity").addEventListener("input", (event) => {
    $("opacity-value").textContent = `${event.target.value}%`;
  });
  $("opacity").addEventListener("change", (event) =>
    send("opacity", { value: event.target.value }),
  );
  for (const id of ["move", "resize"])
    $(id).addEventListener("pointerdown", (event) => {
      if (event.button !== 0) return;
      event.preventDefault();
      send(id);
    });
  document.addEventListener("keydown", (event) => {
    const editing = event.target.matches(
      "input,textarea,[contenteditable=true]",
    );
    if (event.ctrlKey && event.key.toLowerCase() === "f") {
      event.preventDefault();
      $("search").focus();
      $("search").select();
    }
    if (event.ctrlKey && event.key.toLowerCase() === "z" && !editing) {
      event.preventDefault();
      send("undo");
    }
    if (event.key === "Escape" && !document.querySelector("dialog[open]")) {
      document
        .querySelectorAll("details[open]")
        .forEach((node) => node.removeAttribute("open"));
      if ($("search").value) clearSearch();
    }
  });
  document.addEventListener("click", (event) =>
    document.querySelectorAll("details[open]").forEach((node) => {
      if (!node.contains(event.target)) node.removeAttribute("open");
    }),
  );
  document.addEventListener("dragover", (event) => event.preventDefault());
  document.addEventListener("drop", (event) => {
    event.preventDefault();
    clearDrag();
  });
  document.addEventListener("dragend", clearDrag);
  if (window.gsap && window.ScrollTrigger) gsap.registerPlugin(ScrollTrigger);
  motion.addEventListener("change", () => {
    if (motion.matches && window.gsap) {
      window.ScrollTrigger?.getAll().forEach((trigger) => trigger.kill(true));
      gsap.killTweensOf(".category-card,.overview,#feedback");
      gsap.set(".category-card,.overview,#feedback", { clearProps: "all" });
    }
  });
  if (window.ResizeObserver) new ResizeObserver(refreshCardSizes).observe($("grid"));
  bridge?.addEventListener("message", (event) => {
    if (event.data?.type === "state") render(event.data);
    if (event.data?.type === "focusSearch") {
      $("search").focus();
      $("search").select();
    }
  });
  send("ready");
})();
