type Query = { playerId?: string; page?: string };

export function receiptScope({ playerId, page }: Query) {
  const requested = Number(page);
  return {
    playerId: typeof playerId === "string" && /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(playerId) ? playerId.toLowerCase() : "",
    page: Number.isSafeInteger(requested) && requested >= 1 && requested <= 100000 ? requested : 1,
  };
}

export function receiptListHref(playerId: string, page: number) {
  const query = new URLSearchParams();
  if (playerId) query.set("playerId", playerId);
  if (page > 1) query.set("page", String(page));
  return `/guardian/receipts${query.size ? `?${query}` : ""}`;
}

export function receiptDetailHref(id: string, source: string | undefined, playerId: string, page: number) {
  const query = new URLSearchParams();
  if (source === "Tournament") query.set("source", source);
  if (playerId) query.set("playerId", playerId);
  if (page > 1) query.set("page", String(page));
  return `/guardian/receipts/${encodeURIComponent(id)}${query.size ? `?${query}` : ""}`;
}
