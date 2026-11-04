const elements = {
  total: document.querySelector("#total"),
  pending: document.querySelector("#pending"),
  inProgress: document.querySelector("#in-progress"),
  completed: document.querySelector("#completed"),
  blocked: document.querySelector("#blocked"),
  statusFilter: document.querySelector("#status-filter"),
  priorityFilter: document.querySelector("#priority-filter"),
  sortOrder: document.querySelector("#sort-order"),
  items: document.querySelector("#work-items"),
  form: document.querySelector("#work-item-form"),
  refresh: document.querySelector("#refresh"),
  message: document.querySelector("#message"),
  confirmDialog: document.querySelector("#confirm-dialog"),
  confirmTitle: document.querySelector("#confirm-dialog-title"),
  confirmMessage: document.querySelector("#confirm-dialog-message"),
  confirmCancel: document.querySelector("#confirm-dialog-cancel"),
  confirmAccept: document.querySelector("#confirm-dialog-accept")
};

const priorityNames = { Low: "Baja", Medium: "Media", High: "Alta" };
const statusNames = { Pending: "Pendiente", InProgress: "En curso", Completed: "Completada" };
const mutatingIds = new Set();
const startingIds = new Set();
const ownerDrafts = new Map();
const commentDrafts = new Map();
let dashboard = { total: 0, pending: 0, inProgress: 0, completed: 0, items: [] };
let dashboardVersion = 0;
let dashboardRequests = 0;
let creating = false;
let confirmation = null;

function confirmAction({ title, message, confirmLabel = "Confirmar", cancelLabel = "Cancelar", destructive = false }) {
  if (elements.confirmDialog.open || confirmation) return Promise.resolve(false);

  const restoreFocusTo = document.activeElement;
  elements.confirmTitle.textContent = title;
  elements.confirmMessage.textContent = message;
  elements.confirmAccept.textContent = confirmLabel;
  elements.confirmCancel.textContent = cancelLabel;
  elements.confirmDialog.classList.toggle("destructive", destructive);
  elements.confirmDialog.returnValue = "cancel";

  return new Promise(resolve => {
    confirmation = { resolve, restoreFocusTo };
    elements.confirmDialog.showModal();
    elements.confirmCancel.focus();
  });
}

function finishConfirmation() {
  if (!confirmation) return;
  const { resolve, restoreFocusTo } = confirmation;
  confirmation = null;
  resolve(elements.confirmDialog.returnValue === "confirm");
  queueMicrotask(() => {
    if (restoreFocusTo?.isConnected && !restoreFocusTo.disabled) restoreFocusTo.focus();
  });
}

elements.confirmDialog.addEventListener("cancel", () => {
  elements.confirmDialog.returnValue = "cancel";
});
elements.confirmDialog.addEventListener("close", finishConfirmation);

function escapeHtml(value) {
  return String(value).replace(/[&<>'"]/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[character]);
}

function editable(item) {
  return item.status === "Pending" || item.status === "InProgress";
}

function itemButton(id, action, text, className, disabled = false) {
  return `<button type="button" class="${className}" data-action="${action}" data-work-item-id="${escapeHtml(id)}" ${disabled ? "disabled" : ""}>${text}</button>`;
}

function renderOwnerEditor(item, draft, busy) {
  const id = escapeHtml(item.id);
  return `<form class="owner-editor" data-owner-form-id="${id}" aria-label="Cambiar responsable de ${escapeHtml(item.title)}">
    <label for="owner-${id}">Nuevo responsable
      <input id="owner-${id}" data-owner-input-id="${id}" name="owner" value="${escapeHtml(draft.owner)}" required aria-describedby="owner-help-${id} owner-error-${id}" ${busy ? "disabled" : ""}>
    </label>
    <p id="owner-help-${id}" class="owner-help">Obligatorio, máximo 80 caracteres tras quitar espacios extremos. Responsable al abrir: ${escapeHtml(draft.expectedOwner)}.</p>
    <p id="owner-error-${id}" class="owner-error" role="alert">${escapeHtml(draft.error)}</p>
    ${draft.conflict ? `<p class="owner-help">Pulsa Actualizar para revisar la tarea. ${draft.reviewed ? `Responsable actual: ${escapeHtml(item.owner)}.` : "Aún no se ha actualizado esta vista."}</p>
      ${itemButton(item.id, "accept-current", "Usar vista actual", "secondary", busy || !draft.reviewed)}` : ""}
    <div class="owner-editor-actions">
      <button type="submit" data-work-item-id="${id}" data-action="save-owner" ${busy || draft.conflict ? "disabled" : ""}>Guardar</button>
      ${itemButton(item.id, "cancel-owner", "Cancelar", "secondary", busy)}
    </div>
  </form>`;
}

function focusItem(id, action) {
  const candidates = elements.items.querySelectorAll("[data-owner-input-id], [data-comment-input-id], [data-work-item-id]");
  const target = [...candidates].find(element => action === "input"
    ? element.dataset.ownerInputId === id
    : action === "comment-input" ? element.dataset.commentInputId === id
    : element.dataset.workItemId === id && element.dataset.action === action);
  if (target && !target.disabled) target.focus();
  else elements.refresh.focus();
}

function render() {
  const active = document.activeElement;
  const focusedId = active?.dataset.ownerInputId ?? active?.dataset.commentInputId ?? active?.dataset.workItemId;
  const focusedAction = active?.dataset.ownerInputId ? "input" : active?.dataset.commentInputId ? "comment-input" : active?.dataset.action;
  const selection = active?.dataset.ownerInputId || active?.dataset.commentInputId ? [active.selectionStart, active.selectionEnd, active.selectionDirection] : null;
  const existingIds = new Set(dashboard.items.map(item => item.id));
  for (const id of commentDrafts.keys()) {
    if (!existingIds.has(id)) commentDrafts.delete(id);
  }
  const editableIds = new Set(dashboard.items.filter(editable).map(item => item.id));
  for (const id of ownerDrafts.keys()) {
    if (!editableIds.has(id)) ownerDrafts.delete(id);
  }
  elements.total.textContent = dashboard.total;
  elements.pending.textContent = dashboard.pending;
  elements.inProgress.textContent = dashboard.inProgress;
  elements.completed.textContent = dashboard.completed;
  const byId = new Map(dashboard.items.map(item => [item.id, item]));
  // Blocked is derived from Pending and prerequisites; it is never another status.
  for (const item of dashboard.items) {
    item.isBlocked = item.status === "Pending" && (item.prerequisiteIds ?? []).some(id => byId.get(id)?.status !== "Completed");
  }
  elements.blocked.textContent = dashboard.items.filter(item => item.isBlocked).length;
  const visibleItems = BoardView.selectVisibleItems(dashboard.items, {
    status: elements.statusFilter.value,
    priority: elements.priorityFilter.value,
    order: elements.sortOrder.value
  });
  elements.items.innerHTML = visibleItems.length
    ? visibleItems.map(item => {
      const busy = mutatingIds.has(item.id);
      const draft = ownerDrafts.get(item.id);
      return `<div class="work-item ${item.priority.toLowerCase()}" aria-busy="${busy}">
        <div><strong>${escapeHtml(item.title)}</strong><small>${escapeHtml(item.owner)} · ${new Date(item.createdAt).toLocaleDateString("es-ES")}</small></div>
        <div class="work-item-actions">
          <span class="badge">${priorityNames[item.priority]} · ${statusNames[item.status]}</span>
          <label class="priority-editor" for="priority-${escapeHtml(item.id)}">Prioridad
            <select id="priority-${escapeHtml(item.id)}" data-action="priority" data-work-item-id="${escapeHtml(item.id)}" aria-label="Prioridad de ${escapeHtml(item.title)}" ${busy ? "disabled" : ""}>
              ${Object.entries(priorityNames).map(([value, name]) => `<option value="${value}" ${item.priority === value ? "selected" : ""}>${name}</option>`).join("")}
            </select>
          </label>
          ${editable(item) ? itemButton(item.id, "edit-owner", "Cambiar responsable", "secondary change-owner", busy || Boolean(draft)) : ""}
          ${item.status === "Pending" ? itemButton(item.id, "start", startingIds.has(item.id) ? "Iniciando…" : "Iniciar", "secondary change-owner", busy || item.isBlocked) : ""}
          ${editable(item) ? itemButton(item.id, "complete", "Completar", "complete", busy) : ""}
          ${itemButton(item.id, "delete", "Eliminar", "delete", busy)}
        </div>
        ${renderDependencies(item, busy)}
        ${draft ? renderOwnerEditor(item, draft, busy) : ""}
        <form class="comment-editor" data-comment-form-id="${escapeHtml(item.id)}" aria-label="Comentario de ${escapeHtml(item.title)}">
          <label for="comment-${escapeHtml(item.id)}">Comentario (opcional)
            <textarea id="comment-${escapeHtml(item.id)}" data-comment-input-id="${escapeHtml(item.id)}" name="comment" rows="3" aria-describedby="comment-help-${escapeHtml(item.id)}" ${busy ? "disabled" : ""}>\n${escapeHtml(commentDrafts.get(item.id) ?? item.comment ?? "")}</textarea>
          </label>
          <p id="comment-help-${escapeHtml(item.id)}" class="owner-help">Guardar vacío borra el comentario.</p>
          <button type="submit" data-work-item-id="${escapeHtml(item.id)}" data-action="save-comment" ${busy ? "disabled" : ""}>Guardar comentario</button>
        </form>
      </div>`;
    }).join("")
    : '<p class="empty">No hay tareas para esta vista.</p>';
  if (focusedId) {
    focusItem(focusedId, focusedAction);
    if (selection && (document.activeElement?.dataset.ownerInputId === focusedId || document.activeElement?.dataset.commentInputId === focusedId)) {
      document.activeElement.setSelectionRange(...selection);
    }
  }
}

function renderDependencies(item, busy) {
  const dependencies = item.prerequisiteIds ?? [];
  const titles = dependencies.map(id => {
    const dependency = dashboard.items.find(candidate => candidate.id === id);
    return `${dependency?.title ?? id}${dependency?.status === "Completed" ? " (completada)" : " (pendiente de completar)"}`;
  });
  const detail = `<p>${item.isBlocked ? "🔒 Bloqueada · " : ""}${titles.length ? "Prerrequisitos: " + titles.map(escapeHtml).join(", ") : "Sin prerrequisitos"}</p>`;
  if (item.status !== "Pending") return `<div class="dependency-editor">${detail}</div>`;
  return `<div class="dependency-editor">${detail}
    <label for="dependencies-${escapeHtml(item.id)}">Prerrequisitos (selección múltiple; Ctrl para añadir o quitar)
      <select multiple id="dependencies-${escapeHtml(item.id)}" data-dependencies-id="${escapeHtml(item.id)}" ${busy ? "disabled" : ""}>
        ${dashboard.items.filter(candidate => candidate.id !== item.id).map(candidate => `<option value="${escapeHtml(candidate.id)}" ${dependencies.includes(candidate.id) ? "selected" : ""}>${escapeHtml(candidate.title)} · ${statusNames[candidate.status]}</option>`).join("")}
      </select>
    </label>
    ${itemButton(item.id, "save-dependencies", "Guardar prerrequisitos", "secondary", busy)}
    <small>Solo editable mientras está pendiente. Quita todas las selecciones para dejarla sin dependencias.</small>
  </div>`;
}

async function saveDependencies(id) {
  const item = dashboard.items.find(candidate => candidate.id === id);
  if (!item || item.status !== "Pending" || mutatingIds.has(id)) return;
  const selector = [...elements.items.querySelectorAll("[data-dependencies-id]")].find(select => select.dataset.dependenciesId === id);
  const prerequisiteIds = [...selector.selectedOptions].map(option => option.value);
  beginMutation(id);
  try {
    const response = await fetch(`/api/work-items/${id}/dependencies`, {
      method: "PATCH", headers: { "content-type": "application/json" }, body: JSON.stringify({ prerequisiteIds })
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      elements.message.textContent = problem.detail ?? (response.status === 404 ? "La tarea o un prerrequisito ya no existe. Actualiza el tablero." : "No se guardaron los prerrequisitos.");
      return;
    }
    updateLocalItem(id, await response.json());
    await refreshAfterSuccess("Prerrequisitos guardados correctamente.");
  } catch {
    elements.message.textContent = "No se pudo confirmar el guardado. Actualiza antes de volver a intentarlo.";
  } finally {
    endMutation(id);
  }
}

function updateControls() {
  elements.refresh.disabled = creating || mutatingIds.size > 0 || dashboardRequests > 0;
  elements.form.querySelector('button[type="submit"]').disabled = creating;
  elements.items.querySelectorAll("[data-dependencies-id]").forEach(element => { element.disabled = mutatingIds.has(element.dataset.dependenciesId); });
  elements.items.querySelectorAll("[data-owner-input-id], [data-comment-input-id], [data-work-item-id]").forEach(element => {
    const id = element.dataset.ownerInputId ?? element.dataset.commentInputId ?? element.dataset.workItemId;
    const draft = ownerDrafts.get(id);
    element.closest(".work-item").setAttribute("aria-busy", String(mutatingIds.has(id)));
    if (element.dataset.action === "start") {
      element.textContent = startingIds.has(id) ? "Iniciando…" : "Iniciar";
    }
    element.disabled = mutatingIds.has(id)
      || (element.dataset.action === "start" && dashboard.items.find(item => item.id === id)?.isBlocked)
      || (element.dataset.action === "edit-owner" && Boolean(draft))
      || (element.dataset.action === "save-owner" && Boolean(draft?.conflict))
      || (element.dataset.action === "accept-current" && !draft?.reviewed);
  });
}

async function loadDashboard() {
  const version = ++dashboardVersion;
  dashboardRequests++;
  updateControls();
  try {
    const response = await fetch("/api/work-items/dashboard");
    if (!response.ok) throw new Error("No se pudo cargar el tablero.");
    const data = await response.json();
    // Una lectura anterior a otra lectura o mutación nunca reemplaza la vista nueva.
    if (version !== dashboardVersion) return;
    dashboard = data;
    for (const draft of ownerDrafts.values()) {
      if (draft.conflict) draft.reviewed = true;
    }
    render();
  } finally {
    dashboardRequests--;
    updateControls();
  }
}

function beginMutation(id) {
  mutatingIds.add(id);
  dashboardVersion++;
  updateControls();
}

function endMutation(id) {
  mutatingIds.delete(id);
  updateControls();
}

function updateLocalItem(id, replacement) {
  dashboardVersion++;
  const previous = dashboard.items.find(item => item.id === id);
  if (!previous) return;
  if (replacement) dashboard.items = dashboard.items.map(item => item.id === id ? replacement : item);
  else {
    dashboard.items = dashboard.items.filter(item => item.id !== id);
    dashboard.total--;
  }
  if (!replacement || previous.status !== replacement.status) {
    const counters = { Pending: "pending", InProgress: "inProgress", Completed: "completed" };
    dashboard[counters[previous.status]]--;
    if (replacement) dashboard[counters[replacement.status]]++;
  }
  render();
}

async function refreshAfterSuccess(message) {
  elements.message.textContent = message;
  try {
    await loadDashboard();
  } catch {
    elements.message.textContent = `${message} No se pudo actualizar el tablero. Pulsa Actualizar.`;
  }
}

async function changePriority(id, priority) {
  const item = dashboard.items.find(candidate => candidate.id === id);
  if (!item || mutatingIds.has(id) || typeof priority !== "string" || !Object.hasOwn(priorityNames, priority)) return;
  let restorePriorityFocus = document.activeElement?.dataset.workItemId === id
    && document.activeElement?.dataset.action === "priority";
  const trackFocus = event => {
    if (event.target !== document.body
      && (event.target.dataset.workItemId !== id || event.target.dataset.action !== "priority")) {
      restorePriorityFocus = false;
    }
  };
  document.addEventListener("focusin", trackFocus);
  beginMutation(id);
  elements.message.textContent = "Guardando prioridad…";
  try {
    const response = await fetch(`/api/work-items/${id}/priority`, {
      method: "PATCH",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ priority })
    });
    if (!response.ok) {
      elements.message.textContent = response.status === 404 ? "La tarea ya no existe. No se guardó la prioridad. Pulsa Actualizar."
        : response.status === 400 ? "La prioridad no es válida. No se guardó el cambio. Pulsa Actualizar para revisar el tablero."
        : "No se pudo guardar la prioridad. Pulsa Actualizar para revisar el tablero.";
      return;
    }
    const updated = await response.json();
    if (response.status !== 200 || updated?.id !== id || typeof updated?.priority !== "string"
      || !Object.hasOwn(priorityNames, updated.priority)) {
      throw new Error("Resultado de prioridad no confirmado.");
    }
    // A fresh response can include other clients' changes; only priority is this mutation's local authority.
    const current = dashboard.items.find(candidate => candidate.id === id);
    if (current) updateLocalItem(id, { ...current, priority: updated.priority });
    await refreshAfterSuccess("Prioridad guardada correctamente.");
  } catch {
    elements.message.textContent = "No se pudo confirmar el resultado de guardar la prioridad. Pulsa Actualizar antes de volver a intentarlo.";
  } finally {
    document.removeEventListener("focusin", trackFocus);
    endMutation(id);
    render();
    if (restorePriorityFocus) focusItem(id, "priority");
  }
}

async function saveComment(id) {
  const item = dashboard.items.find(candidate => candidate.id === id);
  if (!item || mutatingIds.has(id)) return;
  const comment = commentDrafts.get(id) ?? item.comment ?? "";
  // Retain even an unchanged value if the result cannot be confirmed.
  commentDrafts.set(id, comment);
  const active = document.activeElement;
  const action = active?.dataset.commentInputId === id ? "comment-input"
    : active?.dataset.workItemId === id && active?.dataset.action === "save-comment" ? "save-comment" : null;
  const selection = action === "comment-input" ? [active.selectionStart, active.selectionEnd, active.selectionDirection] : null;
  let restoreFocus = Boolean(action);
  const trackFocus = event => {
    if (event.target !== document.body && event.target.dataset.commentInputId !== id
      && !(event.target.dataset.workItemId === id && event.target.dataset.action === action)) restoreFocus = false;
  };
  document.addEventListener("focusin", trackFocus);
  beginMutation(id);
  elements.message.textContent = "Guardando comentario…";
  try {
    const response = await fetch(`/api/work-items/${id}/comment`, {
      method: "PATCH", headers: { "content-type": "application/json" }, body: JSON.stringify({ comment })
    });
    if (response.status === 404) {
      updateLocalItem(id, null);
      await refreshAfterSuccess("La tarea ya no existe. No se guardó el comentario.");
      return;
    }
    if (response.status !== 200) throw new Error("Guardado no confirmado.");
    const updated = await response.json();
    if (updated?.id !== id || !Object.hasOwn(updated, "comment")
      || (updated.comment !== null && typeof updated.comment !== "string")) throw new Error("Respuesta incompleta.");
    commentDrafts.delete(id);
    const current = dashboard.items.find(candidate => candidate.id === id);
    if (current) updateLocalItem(id, { ...current, comment: updated.comment });
    await refreshAfterSuccess("Comentario guardado correctamente.");
  } catch {
    elements.message.textContent = "No se pudo confirmar el guardado del comentario. Se conserva tu borrador. Pulsa Actualizar antes de volver a intentarlo.";
  } finally {
    document.removeEventListener("focusin", trackFocus);
    endMutation(id);
    if (restoreFocus) {
      focusItem(id, action);
      if (selection && document.activeElement?.dataset.commentInputId === id) document.activeElement.setSelectionRange(...selection);
    }
  }
}

function draftError(id, message) {
  const draft = ownerDrafts.get(id);
  if (draft) draft.error = message;
  elements.message.textContent = message;
  render();
  updateControls();
}

async function saveOwner(id) {
  const item = dashboard.items.find(item => item.id === id);
  const draft = ownerDrafts.get(id);
  if (!item || !editable(item) || !draft || draft.conflict || mutatingIds.has(id)) return;
  const owner = draft.owner.trim();
  if (!owner || owner.length > 80) {
    draftError(id, "El responsable es obligatorio y debe tener como máximo 80 caracteres.");
    focusItem(id, "input");
    return;
  }
  if (!await confirmAction({
    title: "Confirmar cambio de responsable",
    message: `Vas a cambiar el responsable de «${item.title}» de «${draft.expectedOwner}» a «${owner}».`,
    confirmLabel: "Cambiar responsable"
  })) return;
  beginMutation(id);
  elements.message.textContent = "Guardando responsable…";
  let saved = false;
  try {
    const response = await fetch(`/api/work-items/${id}/owner`, {
      method: "PATCH",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ owner, expectedOwner: draft.expectedOwner })
    });
    if (!response.ok) {
      if (response.status === 404) {
        updateLocalItem(id, null);
        await refreshAfterSuccess("La tarea ya no existe. No se guardó el responsable.");
      } else if (response.status === 409) {
        dashboardVersion++;
        draft.conflict = true;
        draft.reviewed = false;
        draftError(id, "No se guardó: la tarea cambió o está completada. Actualiza y revisa la vista antes de confirmar de nuevo.");
      } else if (response.status === 422) {
        draftError(id, "El responsable no es válido. Debe ser obligatorio y tener como máximo 80 caracteres; revisa también la vista original.");
      } else {
        draftError(id, "No se pudo guardar el responsable. Se conserva tu borrador.");
      }
      return;
    }
    saved = true;
    // El 200 confirma el guardado aunque falle la lectura del cuerpo o el refresco.
    const updated = await response.json();
    ownerDrafts.delete(id);
    updateLocalItem(id, updated);
    await refreshAfterSuccess("Responsable guardado correctamente.");
  } catch {
    if (saved) {
      ownerDrafts.delete(id);
      updateLocalItem(id, { ...item, owner });
      elements.message.textContent = "Responsable guardado correctamente. No se pudo leer la respuesta; pulsa Actualizar para revisar el tablero.";
    } else {
      dashboardVersion++;
      draft.conflict = true;
      draft.reviewed = false;
      draftError(id, "No se pudo confirmar el resultado del guardado por un error de red. Se conserva tu borrador; actualiza antes de volver a confirmar.");
    }
  } finally {
    endMutation(id);
    focusItem(id, saved ? "edit-owner" : "input");
  }
}

function validStartResponse(result, id) {
  const item = result?.workItem;
  return typeof result?.changed === "boolean"
    && item?.id === id && item.status === "InProgress"
    && typeof item.title === "string" && typeof item.owner === "string"
    && typeof item.priority === "string" && Object.hasOwn(priorityNames, item.priority)
    && typeof item.createdAt === "string"
    && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/.test(item.createdAt)
    && Number.isFinite(Date.parse(item.createdAt));
}

async function startWorkItem(id) {
  const item = dashboard.items.find(candidate => candidate.id === id);
  if (!item || item.status !== "Pending" || mutatingIds.has(id)) return;
  const input = [...elements.items.querySelectorAll("[data-owner-input-id]")]
    .find(candidate => candidate.dataset.ownerInputId === id);
  const selection = input ? [input.selectionStart, input.selectionEnd, input.selectionDirection] : null;
  startingIds.add(id);
  beginMutation(id);
  elements.message.textContent = "Iniciando tarea…";
  let confirmed = false;
  try {
    const response = await fetch(`/api/work-items/${id}/start`, { method: "PATCH" });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      elements.message.textContent = problem.detail ?? (response.status === 404 ? "La tarea ya no existe. Pulsa Actualizar." : "No se pudo iniciar la tarea. Pulsa Actualizar para revisar su estado.");
      return;
    }
    const result = await response.json();
    if (response.status !== 200 || !validStartResponse(result, id)) {
      throw new Error("Resultado de inicio no confirmado.");
    }
    confirmed = true;
    updateLocalItem(id, result.workItem);
    const message = result.changed ? "Tarea iniciada correctamente." : "La tarea ya está en curso.";
    elements.message.textContent = message;
    try {
      await loadDashboard();
    } catch {
      elements.message.textContent = `${message} No se pudo actualizar el tablero; pulsa Actualizar.`;
    }
  } catch {
    elements.message.textContent = "No se pudo confirmar el resultado de iniciar la tarea. Actualiza antes de volver a intentarlo.";
  } finally {
    startingIds.delete(id);
    endMutation(id);
    if (selection) {
      const currentInput = [...elements.items.querySelectorAll("[data-owner-input-id]")]
        .find(candidate => candidate.dataset.ownerInputId === id);
      currentInput?.setSelectionRange(...selection);
    }
    focusItem(id, confirmed ? "complete" : "start");
  }
}

async function completeWorkItem(id) {
  beginMutation(id);
  elements.message.textContent = "Completando tarea…";
  let completed = false;
  try {
    const response = await fetch(`/api/work-items/${id}/complete`, { method: "PATCH" });
    if (!response.ok) {
      elements.message.textContent = response.status === 404 ? "La tarea ya no existe. Pulsa Actualizar."
        : response.status === 409 ? "La tarea cambió mientras se completaba. Actualiza antes de volver a intentarlo."
        : "No se pudo completar la tarea.";
      return;
    }
    completed = true;
    const item = dashboard.items.find(candidate => candidate.id === id);
    if (item) updateLocalItem(id, { ...item, status: "Completed" });
    await refreshAfterSuccess("Tarea completada correctamente.");
  } catch {
    elements.message.textContent = completed
      ? "Tarea completada correctamente. No se pudo actualizar el tablero; pulsa Actualizar."
      : "No se pudo confirmar el resultado de completar la tarea. Actualiza antes de volver a intentarlo.";
  } finally {
    endMutation(id);
    focusItem(id, "delete");
  }
}

async function deleteWorkItem(id) {
  const item = dashboard.items.find(candidate => candidate.id === id);
  if (!item || !await confirmAction({
    title: "Eliminar tarea",
    message: `Vas a eliminar «${item.title}». Esta acción es permanente y no se puede deshacer.`,
    confirmLabel: "Eliminar",
    destructive: true
  })) return;
  beginMutation(id);
  elements.message.textContent = "Eliminando tarea…";
  try {
    const response = await fetch(`/api/work-items/${id}`, { method: "DELETE" });
    if (response.status !== 204 && response.status !== 404) {
      const problem = await response.json().catch(() => ({}));
      elements.message.textContent = problem.detail ?? "No se pudo eliminar la tarea. Puedes volver a intentarlo.";
      return;
    }
    updateLocalItem(id, null);
    await refreshAfterSuccess(response.status === 204 ? "Tarea eliminada correctamente." : "La tarea ya no existe.");
  } catch {
    elements.message.textContent = "No se pudo confirmar el resultado de la eliminación. Actualiza el tablero antes de volver a intentarlo.";
  } finally {
    endMutation(id);
    elements.refresh.focus();
  }
}

elements.items.addEventListener("input", event => {
  const commentId = event.target.dataset.commentInputId;
  if (commentId && !mutatingIds.has(commentId)) commentDrafts.set(commentId, event.target.value);
  const id = event.target.dataset.ownerInputId;
  const draft = ownerDrafts.get(id);
  if (draft && !mutatingIds.has(id)) draft.owner = event.target.value;
});

elements.items.addEventListener("change", event => {
  if (event.target.dataset.action === "priority") {
    changePriority(event.target.dataset.workItemId, event.target.value);
  }
});

elements.items.addEventListener("submit", event => {
  const commentId = event.target.dataset.commentFormId;
  if (commentId) {
    event.preventDefault();
    saveComment(commentId);
    return;
  }
  const id = event.target.dataset.ownerFormId;
  if (!id) return;
  event.preventDefault();
  saveOwner(id);
});

elements.items.addEventListener("click", event => {
  const button = event.target.closest("button[data-work-item-id][data-action]");
  if (!button || button.disabled || mutatingIds.has(button.dataset.workItemId)) return;
  const id = button.dataset.workItemId;
  const item = dashboard.items.find(candidate => candidate.id === id);
  switch (button.dataset.action) {
    case "edit-owner":
      if (!item || !editable(item) || ownerDrafts.has(id)) return;
      ownerDrafts.set(id, { owner: item.owner, expectedOwner: item.owner, error: "", conflict: false, reviewed: false });
      render();
      focusItem(id, "input");
      break;
    case "cancel-owner":
      ownerDrafts.delete(id);
      render();
      focusItem(id, "edit-owner");
      break;
    case "accept-current": {
      const draft = ownerDrafts.get(id);
      if (!item || !editable(item) || !draft?.conflict || !draft.reviewed) return;
      draft.expectedOwner = item.owner;
      draft.conflict = false;
      draft.reviewed = false;
      draft.error = "";
      render();
      focusItem(id, "input");
      break;
    }
    case "save-dependencies":
      saveDependencies(id);
      break;
    case "start":
      startWorkItem(id);
      break;
    case "complete":
      if (item && editable(item)) completeWorkItem(id);
      break;
    case "delete":
      deleteWorkItem(id);
      break;
    // Guardar lo gestiona submit; una acción desconocida nunca completa la tarea.
  }
});

elements.form.addEventListener("submit", async event => {
  event.preventDefault();
  if (creating) return;
  creating = true;
  dashboardVersion++;
  updateControls();
  elements.message.textContent = "Guardando…";
  let created = false;
  try {
    const formData = new FormData(elements.form);
    const response = await fetch("/api/work-items", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(Object.fromEntries(formData))
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      elements.message.textContent = problem.detail ?? "No se pudo crear la tarea.";
      return;
    }
    created = true;
    dashboardVersion++;
    elements.form.reset();
    await refreshAfterSuccess("Tarea creada correctamente.");
  } catch {
    elements.message.textContent = created
      ? "Tarea creada correctamente. No se pudo actualizar el tablero; pulsa Actualizar."
      : "No se pudo confirmar el resultado de la creación. Actualiza el tablero antes de volver a intentarlo.";
  } finally {
    creating = false;
    updateControls();
  }
});

const savedView = BoardView.loadPreferences();
elements.statusFilter.value = savedView.status;
elements.priorityFilter.value = savedView.priority;
elements.sortOrder.value = savedView.order;
function changeView() {
  BoardView.savePreferences({
    status: elements.statusFilter.value,
    priority: elements.priorityFilter.value,
    order: elements.sortOrder.value
  });
  render();
}
elements.statusFilter.addEventListener("change", changeView);
elements.priorityFilter.addEventListener("change", changeView);
elements.sortOrder.addEventListener("change", changeView);

elements.refresh.addEventListener("click", () => {
  if (elements.refresh.disabled) return;
  loadDashboard().catch(() => { elements.message.textContent = "No se pudo actualizar el tablero. Se conservan los borradores editables."; });
});
loadDashboard().catch(() => { elements.message.textContent = "No se pudo cargar el tablero."; });
