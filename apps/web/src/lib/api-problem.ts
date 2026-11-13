function readableMessage(value: unknown): string | undefined {
  if (typeof value !== "string" || !/\p{Script=Arabic}/u.test(value)) return;
  const message = value.trim();
  if (message.length > 600 || /[\r\n]|<[^>]*>/.test(message)) return;
  return message || undefined;
}

/** Preserve localized transport guidance and the native network error kind. */
export function apiTransportError(cause: unknown, fallback: string): Error {
  const message = cause instanceof Error ? readableMessage(cause.message) : undefined;
  const ErrorType = cause instanceof TypeError ? TypeError : Error;
  return new ErrorType(message ?? fallback);
}

/** Keep the API's Arabic validation guidance; never expose framework error text. */
export async function apiProblemMessage(response: Response, fallback: string): Promise<string> {
  return (await apiProblem(response, fallback)).message;
}

export async function apiProblem(response: Response, fallback: string): Promise<{ message: string; code?: string }> {
  const problem: unknown = await response.json().catch(() => null);
  if (typeof problem !== "object" || problem === null) return { message: fallback };
  const { message, detail, title, errors, code } = problem as Record<string, unknown>;
  // For service failures accept only a deliberately localized message, not
  // framework details (which can contain stack traces and internal paths).
  if (response.status >= 500) {
    const localized = readableMessage(message);
    return { message: localized && !/[a-z]/i.test(localized) ? localized : fallback };
  }
  const validation = Array.isArray(errors) ? errors : errors && typeof errors === "object" ? Object.values(errors).flat() : [];
  return {
    message: readableMessage(message) ?? readableMessage(detail)
      ?? ([...new Set(validation.map(readableMessage).filter(Boolean))].join(" ") || readableMessage(title) || fallback),
    code: typeof code === "string" ? code : undefined,
  };
}

/** A failed token request precedes any write; a lost write response is ambiguous. */
export async function requestToken(path: string, signal?: AbortSignal) {
  const response = await fetch(path, { cache: "no-store", ...(signal ? { signal } : {}) });
  const value: unknown = response.ok ? await response.json().catch(() => null) : null;
  const token = value && typeof value === "object" && "token" in value ? value.token : undefined;
  return { status: response.status, token: typeof token === "string" && token.trim() ? token : undefined };
}
