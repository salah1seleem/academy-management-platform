export const paymentQueuePath = "/dashboard/subscriptions/payments";
export const paymentQueueTypes = ["Enrollment", "Renewal", "Product", "Tournament"] as const;
export const paymentQueueStatuses = ["Pending", "Approved", "Rejected", "Cancelled"] as const;
export const paymentQueuePageSizes = [10, 25, 50] as const;
export const paymentQueueReferencePattern = /^(?:[a-f\d]{12}|[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12})$/i;

type Query = Pick<URLSearchParams, "get">;
export type PaymentQueueScope = {
  type: string; status: string; from: string; to: string; branchId: string;
  page: number; pageSize: number; reference: string;
};

export function paymentQueueScope(query: Query | null, defaults: { from: string; to: string }): PaymentQueueScope {
  const pick = (key: string, values: readonly string[], fallback = "") => {
    const value = query?.get(key);
    return value === "" || value && values.includes(value) ? value : fallback;
  };
  const date = (key: "from" | "to") => {
    const value = query?.get(key);
    if (value === "" || value && /^\d{4}-\d{2}-\d{2}$/.test(value)) return value;
    return query?.get("allDates") === "true" ? "" : defaults[key];
  };
  const page = Number(query?.get("page") ?? "1");
  const size = Number(query?.get("pageSize") ?? "25");
  const branch = query?.get("branchId") ?? "";
  return {
    type: pick("type", paymentQueueTypes), status: pick("status", paymentQueueStatuses, "Pending"),
    from: date("from"), to: date("to"), branchId: /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(branch) ? branch : "",
    page: Number.isInteger(page) && page >= 1 && page <= 100000 ? page : 1,
    pageSize: paymentQueuePageSizes.some(value => value === size) ? size : 25,
    reference: (query?.get("reference") ?? "").trim().slice(0, 37),
  };
}

export function paymentQueueQuery(scope: PaymentQueueScope) {
  const { type, status, from, to, branchId, page, pageSize, reference } = scope;
  return new URLSearchParams({
    ...(type ? { type } : {}), status, from, to, ...(branchId ? { branchId } : {}),
    ...(!from && !to ? { allDates: "true" } : {}),
    ...(reference ? { reference } : {}),
    page: String(page), pageSize: String(pageSize),
  });
}

export function paymentQueueBack(query: Query | null) {
  if (query?.get("origin") !== "payment-queue") return undefined;
  const scope = paymentQueueScope(query, { from: "", to: "" });
  return { href: `${paymentQueuePath}?${paymentQueueQuery(scope)}`, label: "العودة إلى طلبات الدفع" };
}

const enrollmentDetailsPath = "/dashboard/enrollment-requests";
const enrollmentScopeKeys = ["q", "status", "sportId", "branchId", "from", "to", "allDates"];

export function enrollmentReceiptQuery(id: string, query: Query) {
  const scope = query.get("origin") === "payment-queue"
    ? paymentQueueQuery(paymentQueueScope(query, { from: "", to: "" }))
    : new URLSearchParams(enrollmentScopeKeys.flatMap(key => {
      const value = query.get(key);
      return value ? [[key, value]] : [];
    }));
  if (query.get("origin") === "payment-queue") scope.set("origin", "payment-queue");
  scope.set("enrollmentRequest", id);
  return scope;
}

export function enrollmentReceiptBack(query: Query) {
  const id = query.get("enrollmentRequest");
  if (!id || !/^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(id)) return undefined;
  const scope = enrollmentReceiptQuery(id, query);
  scope.delete("enrollmentRequest");
  return {
    label: "العودة إلى طلب الاشتراك",
    href: `${enrollmentDetailsPath}/${id}${scope.size ? `?${scope}` : ""}`,
  };
}
