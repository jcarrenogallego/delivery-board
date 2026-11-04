(function (root) {
  const priorityRank = { High: 0, Medium: 1, Low: 2 };

  function selectVisibleItems(items, { status = "All", priority = "All", order = "base" } = {}) {
    const visible = items.filter(item =>
      (status === "All" || (status === "Blocked" ? item.status === "Pending" && item.isBlocked : item.status === status))
      && (priority === "All" || (item.priority ?? "Medium") === priority));
    // filter preserves the server's base order; equal priorities keep that order.
    if (order === "priority") visible.sort((left, right) =>
      priorityRank[left.priority ?? "Medium"] - priorityRank[right.priority ?? "Medium"]);
    return visible;
  }

  const storageKey = "deliveryboard.view-preferences.v1";

  function validPreferences(value) {
    return {
      status: ["All", "Pending", "InProgress", "Completed", "Blocked"].includes(value?.status) ? value.status : "All",
      priority: ["All", "Low", "Medium", "High"].includes(value?.priority) ? value.priority : "All",
      order: ["base", "priority"].includes(value?.order) ? value.order : "base"
    };
  }

  function loadPreferences() {
    try {
      return validPreferences(JSON.parse(root.localStorage.getItem(storageKey)));
    } catch {
      return validPreferences(null);
    }
  }

  function savePreferences(value) {
    try {
      root.localStorage.setItem(storageKey, JSON.stringify(validPreferences(value)));
    } catch {
      // Browsers can deny storage; filters still work for the current page.
    }
  }
  const api = { selectVisibleItems, loadPreferences, savePreferences };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else root.BoardView = api;
})(globalThis);
