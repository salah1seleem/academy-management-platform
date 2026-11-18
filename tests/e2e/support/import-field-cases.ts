import { expect } from "@playwright/test";
import { afterDays } from "./placement";
import { editImportCells, readImportCells, type FixtureEdit } from "./import-cell-fixture";

export const columns = "ABCDEFGHIJKLMNOPQRS".split("");
export type PlayerFixtureRow = { row: number; cells: Record<string, string> };
export function playerRows(book: Buffer): PlayerFixtureRow[] {
  const cells = readImportCells(book, "اللاعبون");
  const numbers = [...new Set([...cells.keys()].map(key => Number(key.replace(/^[A-Z]+/, ""))))].filter(n => n > 1).sort((a, b) => a - b);
  return numbers.map(row => ({ row, cells: Object.fromEntries(columns.map(column => [column, cells.get(`${column}${row}`)?.value ?? ""])) })).filter(row => Object.values(row.cells).some(Boolean));
}
export function fieldCohort(original: Buffer) {
  const originalCells = readImportCells(original, "اللاعبون"), originals = playerRows(original);
  expect(originals.map(row => row.cells.A)).toEqual(Array.from({ length: 50 }, (_, i) => `PL${String(i + 1).padStart(2, "0")}`));
  const value = (cell: string) => originalCells.get(cell)!.value;
  const required = [["R01", "A23"], ["R02", "B2"], ["R03", "C3"], ["R04", "D5"], ["R05", "E4"], ["R06", "H6"], ["R07", "I7"], ["R08", "J8"], ["R09", "K9"], ["R10", "L10"], ["R11", "Q11"]] as const;
  const corrupt = [
    { id: "D01", cell: "D13", value: "0101234567899" }, { id: "D02", cell: "D15", value: "0101234567" },
    { id: "D03", cell: "D17", value: "01912345678" }, { id: "D04", cell: "D19", value: value("D19").slice(1), isText: false },
    { id: "D05", cell: "D21", value: "+441234567890" }, { id: "D06", cell: "E12", value: "2026-02-30" },
    { id: "D07", cell: "L14", value: "2026-13-01" }, { id: "D08", cell: "E16", value: afterDays(value("L16"), 1), issueCell: "L16" },
    { id: "D09", cell: "F18", value: "غير ذلك" }, { id: "D10", cell: "G20", value: "https://invalid.example/p.png" },
    { id: "D11", cell: "A22", value: "PL!21" },
  ];
  const failures = [...required.map(([id, cell]) => ({ id: `IF-${id}`, cell, value: "", isText: true, issueCell: cell })), ...corrupt.map(entry => ({ ...entry, id: `IF-${entry.id}`, isText: entry.isText ?? true, issueCell: entry.issueCell ?? entry.cell }))];
  const edits: FixtureEdit[] = failures.map(({ cell, value, isText }) => ({ sheet: "اللاعبون", cell, value, isText }));
  for (const column of ["F", "G", "M", "N", "O", "P", "R", "S"]) edits.push({ sheet: "اللاعبون", cell: `${column}24`, value: "" });
  for (const cell of ["D25", "D26", "D27", "D28"]) expect(value(cell)).toMatch(/^01[0125]\d{8}$/);
  edits.push({ sheet: "اللاعبون", cell: "D25", value: `+20${value("D25").slice(1)}` }, { sheet: "اللاعبون", cell: "D26", value: `+20 ${value("D26")}` },
    { sheet: "اللاعبون", cell: "D27", value: value("D27").replace(/\d/g, digit => "٠١٢٣٤٥٦٧٨٩"[Number(digit)]) },
    { sheet: "اللاعبون", cell: "D28", value: ` ${value("D28").slice(0, 3)}-${value("D28").slice(3)} ` });
  const mixed = editImportCells(original, edits);
  return { original, originals, mixed, failures: failures.map(entry => ({ ...entry, row: Number(entry.cell.replace(/^[A-Z]+/, "")), issueRow: Number(entry.issueCell.replace(/^[A-Z]+/, "")), field: originalCells.get(`${entry.issueCell.replace(/\d+$/, "")}1`)!.value })) };
}
export function repairCohort(correction: Buffer, original: Buffer) {
  const originals = playerRows(original), rows = playerRows(correction);
  // The selected correction file owns exactly the first22 original records.
  expect(originals).toHaveLength(50); expect(rows).toHaveLength(22);
  const changes = rows.flatMap((row, index) => columns.map(column => ({ sheet: "اللاعبون", cell: `${column}${row.row}`, value: originals[index].cells[column] })));
  return editImportCells(correction, changes);
}
export function parseCsv(value: string) {
  const rows: string[][] = []; let row: string[] = [], field = "", quoted = false;
  const text = value.replace(/^\uFEFF/, "").replace(/\r\n/g, "\n");
  for (let index = 0; index < text.length; index++) {
    const char = text[index];
    if (char === '"') { if (quoted && text[index + 1] === '"') { field += '"'; index++; } else quoted = !quoted; }
    else if (!quoted && (char === "," || char === "\n")) { row.push(field); field = ""; if (char === "\n") { rows.push(row); row = []; } }
    else field += char;
  }
  if (field || row.length) { row.push(field); rows.push(row); }
  expect(quoted).toBe(false); return rows;
}
