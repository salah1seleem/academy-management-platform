const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === "object" && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const count = (value: unknown, minimum = 0): value is number => typeof value === "number" && Number.isSafeInteger(value) && value >= minimum;
const date = (value: unknown) => {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const parsed = Date.parse(`${value}T12:00:00Z`);
  return Number.isFinite(parsed) && new Date(parsed).toISOString().slice(0, 10) === value;
};
const optional = (value: unknown, valid: (value: unknown) => boolean) => value == null || valid(value);
const list = (value: unknown, valid: (value: unknown) => boolean) => Array.isArray(value) && value.every(valid);

function issue(value: unknown) {
  return object(value) && text(value.sheet) && count(value.row) && typeof value.column === "string" && text(value.message)
    && optional(value.sheetLabel, text) && optional(value.columnLabel, text);
}
function row(value: unknown) {
  return object(value) && count(value.row, 2)
    && ["legacyPlayerId", "legacyEnrollmentId", "playerName", "guardianName", "guardianPhone", "currency", "existingNew"].every(key => text(value[key]))
    && optional(value.start, date) && optional(value.end, date)
    && optional(value.agreedNet, amount => text(amount) && Number.isFinite(Number(amount)) && Number(amount) >= 0);
}
function clean(value: unknown) {
  return object(value) && count(value.playerCount) && count(value.excludedRows) && optional(value.relatedRows, count);
}

// Validate the read contract before rendering or offering a financial/identity write.
// rows may be a bounded preview, so its length is not the total registration count.
export function validImportPreview(value: unknown): boolean {
  if (!object(value) || !["schemaVersion", "fileHash", "referenceStamp"].every(key => text(value[key]))
    || !optional(value.sourceSystem, source => typeof source === "string") || !optional(value.cutoffDate, date)
    || !["rowCount", "playerCount", "householdCount", "financiallyIncomplete"].every(key => count(value[key]))
    || typeof value.canApprove !== "boolean" || !list(value.errors, issue) || !list(value.rows, row)
    || value.canApprove && (value.errors as unknown[]).length > 0 || !optional(value.cleanRows, clean)) return false;
  if (value.canApprove && (!text(value.sourceSystem) || !date(value.cutoffDate))) return false;
  if (!["guardianCount", "guardianRelationships", "coachAssignments", "tournamentParticipations", "historicalPayments"].every(key => optional(value[key], count))) return false;
  if (!optional(value.historicalPaymentTotal, amount => typeof amount === "number" && Number.isFinite(amount) && amount >= 0)
    || !optional(value.historicalPaymentCurrency, text)) return false;
  if (!optional(value.historical, data => object(data) && ["periods", "sessions", "attendance", "evaluations", "published", "drafts", "scores"].every(key => count(data[key])))) return false;
  if (!optional(value.effects, data => list(data, effect => object(effect) && text(effect.sheet) && count(effect.create) && count(effect.reuse)))) return false;
  if (!optional(value.accountLinks, data => list(data, link => object(link) && ["role", "name", "phone"].every(key => text(link[key]))))) return false;
  if (!optional(value.guardianMatches, data => list(data, match => object(match) && ["householdKey", "guardianName", "phone"].every(key => text(match[key])) && list(match.candidates, text)))) return false;
  return optional(value.registrationLabels, labels => object(labels) && Object.values(labels).every(label => object(label) && ["sport", "group", "branch"].every(key => typeof label[key] === "string")));
}
