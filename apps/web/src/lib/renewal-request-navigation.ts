import { paymentQueueBack, paymentQueuePath, paymentQueueQuery, paymentQueueScope } from "./payment-queue-navigation";

const renewalListPath = "/dashboard/subscriptions/renewals";
const optionalColumns = ["context", "amount", "status"];
type Query = Pick<URLSearchParams, "get">;
type RenewalListScope = { term: string; page: number; pageSize: number; hidden: string[] };

export function renewalListScope(search: Query): RenewalListScope {
  const size = search.get("pageSize");
  const page = search.get("page") ?? "";
  const hidden = search.get("hide")?.split(",") ?? [];
  return {
    term: search.get("q")?.slice(0, 120) ?? "",
    page: /^[1-9]\d{0,5}$/.test(page) ? Number(page) : 1,
    pageSize: size === "10" || size === "50" ? Number(size) : 25,
    hidden: optionalColumns.filter(column => hidden.includes(column)),
  };
}

function renewalListQuery({ term, page, pageSize, hidden }: RenewalListScope) {
  return new URLSearchParams({
    ...(term ? { q: term } : {}),
    ...(page > 1 ? { page: String(page) } : {}),
    ...(pageSize !== 25 ? { pageSize: String(pageSize) } : {}),
    ...(hidden.length ? { hide: hidden.join(",") } : {}),
  });
}

export function renewalListHref(scope: RenewalListScope) {
  return withQuery(renewalListPath, renewalListQuery(scope));
}

export function renewalDetailHref(id: string, scope: RenewalListScope) {
  return withQuery(`${renewalListPath}/${encodeURIComponent(id)}`, renewalListQuery(scope));
}

export function renewalFromPaymentHref(id: string, query: Query) {
  const params = queueQuery(query);
  params.set("fromPayment", "true");
  return withQuery(`${renewalListPath}/${encodeURIComponent(id)}`, params);
}

export function renewalPaymentHref(id: string, query: Query) {
  return withQuery(`${paymentQueuePath}/${encodeURIComponent(id)}`, queueQuery(query));
}

export function renewalReceiptQuery(id: string, query: Query) {
  const params = queueQuery(query);
  params.set("renewalPayment", id);
  return params;
}

export function renewalReceiptBack(query: Query) {
  const id = query.get("renewalPayment");
  if (!id || !/^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(id)) return undefined;
  return { href: renewalPaymentHref(id, query), label: "العودة إلى طلب الدفع" };
}

export function renewalDetailBack(query: Query, currentPaymentId?: string | null) {
  if (query.get("fromPayment") !== "true") {
    return { href: renewalListHref(renewalListScope(query)), label: "العودة إلى طلبات التجديد" };
  }
  if (currentPaymentId) {
    return { href: renewalPaymentHref(currentPaymentId, query), label: "العودة إلى طلب الدفع" };
  }
  return paymentQueueBack(query) ?? { href: paymentQueuePath, label: "العودة إلى طلبات الدفع" };
}

function queueQuery(query: Query) {
  if (!paymentQueueBack(query)) return new URLSearchParams();
  const params = paymentQueueQuery(paymentQueueScope(query, { from: "", to: "" }));
  params.set("origin", "payment-queue");
  return params;
}

function withQuery(href: string, query: URLSearchParams) {
  return query.size ? `${href}?${query}` : href;
}
