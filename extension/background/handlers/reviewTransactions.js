function extractSheetRowId(sheetWriteResult) {
  const updatedRange = String(sheetWriteResult?.updatedRange || "").trim();
  const match = updatedRange.match(/!A(\d+):/);
  return match?.[1] ? `row:${match[1]}` : updatedRange;
}

async function handleFetchPendingReviewTransactions() {
  const result = await BackgroundCore.fetchPendingReviewTransactions();
  return {
    ok: true,
    total: Number(result?.total || 0),
    entries: Array.isArray(result?.entries) ? result.entries : [],
  };
}

async function handleApproveReviewTransaction(msg) {
  const transactionId = String(msg?.transactionId || "").trim();
  if (!transactionId) {
    throw new Error("Transaction id is required.");
  }

  const { sheetUrl = "", sheetTab = "draft" } = await chrome.storage.sync.get([
    "sheetUrl",
    "sheetTab",
  ]);
  if (!sheetUrl) {
    throw new Error("Configura la URL de Google Sheets antes de aprobar la transacción.");
  }

  const approvedResult = await BackgroundCore.reviewTransactionAction(transactionId, {
    action: "approve",
    date: msg?.date || "",
    amount: msg?.amount || "",
    category: msg?.category || "",
    description: msg?.description || "",
  });
  const approvedEntry = approvedResult?.entry;
  if (!approvedEntry) {
    throw new Error("El servidor no devolvió la transacción aprobada.");
  }

  const token = await BackgroundCore.getBackendAuthToken();
  const sheetWriteResult = await BackgroundCore.appendParsedEntriesToSheet(
    token,
    sheetUrl,
    sheetTab,
    [approvedEntry],
  );
  const sheetRowId = extractSheetRowId(sheetWriteResult);

  const syncedResult = await BackgroundCore.reviewTransactionAction(transactionId, {
    action: "mark_sheet_synced",
    sheetRowId,
  });

  return {
    ok: true,
    entry: syncedResult?.entry || approvedEntry,
    sheetRowId,
    sheetWriteResult,
  };
}

self.handleFetchPendingReviewTransactions = handleFetchPendingReviewTransactions;
self.handleApproveReviewTransaction = handleApproveReviewTransaction;
