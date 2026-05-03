const DEFAULT_CATEGORIES = [
  "Casa",
  "Comida",
  "Transporte",
  "Salud",
  "Servicios",
  "Transaccion/SINPE",
];

const REVIEW_REASONS = {
  confidence_score_below_auto_approval_threshold: "La confianza quedó por debajo del umbral de aprobación automática.",
  confidence_score_missing: "La transacción no tiene score de confianza.",
};

const MESSAGE_TYPES = {
  FETCH_PENDING_REVIEW_TRANSACTIONS: "FETCH_PENDING_REVIEW_TRANSACTIONS",
  APPROVE_REVIEW_TRANSACTION: "APPROVE_REVIEW_TRANSACTION",
};

document.addEventListener("DOMContentLoaded", async () => {
  const pendingCountEl = document.getElementById("pendingCount");
  const approvedCountEl = document.getElementById("approvedCount");
  const reviewStatusEl = document.getElementById("reviewStatus");
  const pendingListEl = document.getElementById("pendingList");
  const emptyStateEl = document.getElementById("emptyState");
  const detailEmptyEl = document.getElementById("detailEmpty");
  const reviewFormEl = document.getElementById("reviewForm");
  const detailTitleEl = document.getElementById("detailTitle");
  const detailSubtitleEl = document.getElementById("detailSubtitle");
  const confidenceBadgeEl = document.getElementById("confidenceBadge");
  const dateInputEl = document.getElementById("dateInput");
  const amountInputEl = document.getElementById("amountInput");
  const categoryInputEl = document.getElementById("categoryInput");
  const descriptionInputEl = document.getElementById("descriptionInput");
  const reviewReasonEl = document.getElementById("reviewReason");
  const emailContextEl = document.getElementById("emailContext");
  const refreshBtn = document.getElementById("refreshBtn");
  const resetBtn = document.getElementById("resetBtn");

  let categories = [...DEFAULT_CATEGORIES];
  let pendingItems = [];
  let approvedItems = [];
  let selectedId = "";

  async function loadCategories() {
    const stored = await chrome.storage.sync.get(["categories"]);
    const configured = Array.isArray(stored.categories)
      ? stored.categories.map((item) => String(item || "").trim()).filter(Boolean)
      : [];
    categories = configured.length > 0 ? configured : [...DEFAULT_CATEGORIES];
  }

  async function fetchPendingReviewItems() {
    const result = await chrome.runtime.sendMessage({
      type: MESSAGE_TYPES.FETCH_PENDING_REVIEW_TRANSACTIONS,
    });
    if (!result?.ok) {
      throw new Error(result?.error || "No se pudieron cargar las transacciones pendientes.");
    }

    return Array.isArray(result.entries) ? result.entries : [];
  }

  function renderCategoryOptions(selectedCategory) {
    const allCategories = new Set([...categories, selectedCategory].filter(Boolean));
    categoryInputEl.replaceChildren(
      ...Array.from(allCategories).map((category) => {
        const option = document.createElement("option");
        option.value = category;
        option.textContent = category;
        return option;
      }),
    );
    categoryInputEl.value = selectedCategory || "";
  }

  function formatConfidence(score) {
    if (typeof score !== "number") return "Sin score";
    return `${Math.round(score * 100)}%`;
  }

  function getReasonText(item) {
    return REVIEW_REASONS[item.review_reason] ||
      item.review_reason ||
      "Requiere revisión antes de enviarse a Google Sheets.";
  }

  function getSelectedItem() {
    return pendingItems.find((item) => item.id === selectedId) || null;
  }

  function renderCounts() {
    pendingCountEl.textContent = String(pendingItems.length);
    approvedCountEl.textContent = String(approvedItems.length);
    reviewStatusEl.textContent = pendingItems.length > 0 ? "En revisión" : "Sin pendientes";
  }

  function renderPendingList() {
    pendingListEl.replaceChildren(
      ...pendingItems.map((item) => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = `pending-card${item.id === selectedId ? " active" : ""}`;
        button.dataset.id = item.id;

        const title = document.createElement("strong");
        title.textContent = item.description || "Sin descripción";

        const detail = document.createElement("span");
        detail.textContent = `${item.amount || "0"} · ${item.category || "Sin categoría"}`;

        const meta = document.createElement("div");
        meta.className = "pending-meta";

        const date = document.createElement("span");
        date.textContent = item.date || "Sin fecha";

        const score = document.createElement("span");
        score.textContent = formatConfidence(item.confidence_score);

        meta.append(date, score);
        button.append(title, detail, meta);

        button.addEventListener("click", () => {
          selectedId = item.id;
          render();
        });

        return button;
      }),
    );

    emptyStateEl.classList.toggle("hidden", pendingItems.length > 0);
  }

  function renderDetail() {
    const item = getSelectedItem();
    detailEmptyEl.classList.toggle("hidden", !!item);
    reviewFormEl.classList.toggle("hidden", !item);

    if (!item) return;

    detailTitleEl.textContent = item.description || "Sin descripción";
    detailSubtitleEl.textContent = `${item.sender || "Remitente desconocido"} · ${item.subject || "Sin asunto"}`;
    confidenceBadgeEl.textContent = formatConfidence(item.confidence_score);
    dateInputEl.value = item.date || "";
    amountInputEl.value = item.amount || "";
    descriptionInputEl.value = item.description || "";
    renderCategoryOptions(item.category || "");
    reviewReasonEl.textContent = getReasonText(item);
    emailContextEl.textContent = `${item.subject || "Sin asunto"} desde ${item.sender || "remitente desconocido"}`;
  }

  function render() {
    renderCounts();
    renderPendingList();
    renderDetail();
  }

  function readFormValues() {
    return {
      date: dateInputEl.value || "",
      amount: amountInputEl.value.trim(),
      category: categoryInputEl.value.trim(),
      description: descriptionInputEl.value.trim(),
    };
  }

  function validateForm(values) {
    if (!values.amount || Number.isNaN(Number(values.amount))) {
      return "El monto debe ser numérico.";
    }
    if (!values.category) {
      return "Selecciona una categoría.";
    }
    if (!values.description) {
      return "Agrega una descripción.";
    }
    return "";
  }

  async function approveSelected() {
    const item = getSelectedItem();
    if (!item) return;

    const values = readFormValues();
    const validationError = validateForm(values);
    if (validationError) {
      reviewStatusEl.textContent = validationError;
      return;
    }

    reviewStatusEl.textContent = "Aprobando";
    const result = await chrome.runtime.sendMessage({
      type: MESSAGE_TYPES.APPROVE_REVIEW_TRANSACTION,
      transactionId: item.id,
      ...values,
    });
    if (!result?.ok) {
      throw new Error(result?.error || "No se pudo aprobar la transacción.");
    }

    pendingItems = pendingItems.filter((pending) => pending.id !== item.id);
    approvedItems = [result.entry || { ...item, ...values }, ...approvedItems];
    selectedId = pendingItems[0]?.id || "";
    render();
  }

  async function loadPendingReview() {
    try {
      reviewStatusEl.textContent = "Cargando";
      await loadCategories();
      pendingItems = await fetchPendingReviewItems();
      approvedItems = [];
      selectedId = pendingItems[0]?.id || "";
      render();
    } catch (err) {
      pendingItems = [];
      selectedId = "";
      render();
      reviewStatusEl.textContent = "No se pudo cargar";
    }
  }

  reviewFormEl.addEventListener("submit", async (event) => {
    event.preventDefault();
    try {
      await approveSelected();
    } catch (err) {
      reviewStatusEl.textContent = err?.message || "No se pudo aprobar la transacción.";
    }
  });

  resetBtn.addEventListener("click", () => {
    renderDetail();
  });

  refreshBtn.addEventListener("click", async () => {
    await loadPendingReview();
  });

  await loadPendingReview();
});
