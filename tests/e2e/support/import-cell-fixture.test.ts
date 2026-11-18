import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { test } from "node:test";
import { editImportCells, readImportCells } from "./import-cell-fixture.ts";
import { ownImportWorkbook } from "./import-workbook.ts";

const source = readFileSync(new URL("../../../apps/api/src/Academy.Api/Assets/academy-completed-sample.xlsx", import.meta.url));
const digest = (value: Buffer) => createHash("sha256").update(value).digest("hex");
const originalHash = digest(source);
function entries(input: Buffer) { const data = new Map<string, Buffer>(); ownImportWorkbook(input, {}, (name, bytes) => { data.set(name, bytes); return bytes; }); return data; }

test("reads all fifty stable identities and preserves the untouched workbook", () => {
  const cells = readImportCells(source, "اللاعبون");
  assert.equal(cells.get("A2")?.value, "PL01"); assert.equal(cells.get("A51")?.value, "PL50");
  assert.equal(cells.get("D2")?.isText, true); assert.equal(digest(source), originalHash);
});
test("changes one exact cell and preserves metadata, unrelated sheets and every other player cell", () => {
  const changed = editImportCells(source, [{ sheet: "اللاعبون", cell: "B2", value: "" }]);
  const before = readImportCells(source, "اللاعبون"), after = readImportCells(changed, "اللاعبون");
  assert.equal(after.get("B2")?.value, "");
  for (const [cell, value] of before) if (cell !== "B2") assert.deepEqual(after.get(cell), value);
  const previous = entries(source), next = entries(changed);
  for (const [name, bytes] of previous) if (name !== "xl/worksheets/sheet2.xml") assert.deepEqual(next.get(name), bytes, name);
  assert.equal(digest(source), originalHash);
});
test("retains a numeric cell type instead of silently restoring a leading zero", () => {
  const value = readImportCells(source, "اللاعبون").get("D2")!.value.slice(1);
  const changed = editImportCells(source, [{ sheet: "اللاعبون", cell: "D2", value, isText: false }]);
  assert.deepEqual(readImportCells(changed, "اللاعبون").get("D2"), { value, isText: false });
});
for (const value of ["", "+20 01257000001", " 012-5700-0001 ", "٠١٢٥٧٠٠٠٠٠١", "<&> test"]) test(`round-trips a controlled text value ${JSON.stringify(value)}`, () => {
  assert.deepEqual(readImportCells(editImportCells(source, [{ sheet: "اللاعبون", cell: "D2", value }]), "اللاعبون").get("D2"), { value, isText: true });
});
test("rejects an unknown sheet, missing cell, invalid address and duplicate edit", () => {
  assert.throws(() => editImportCells(source, [{ sheet: "Missing", cell: "A2", value: "x" }]), /Missing synthetic sheet/);
  assert.throws(() => editImportCells(source, [{ sheet: "اللاعبون", cell: "ZZ999", value: "x" }]), /Missing synthetic cell/);
  assert.throws(() => editImportCells(source, [{ sheet: "اللاعبون", cell: 'A2"', value: "x" }]), /Invalid synthetic cell address/);
  assert.throws(() => editImportCells(source, [{ sheet: "اللاعبون", cell: "A2", value: "x" }, { sheet: "اللاعبون", cell: "A2", value: "y" }]), /Ambiguous duplicate/);
});
test("also reads and edits default-namespace output from the server writer", () => {
  const plain = ownImportWorkbook(source, {}, (name, bytes) => name.endsWith(".xml") ? Buffer.from(bytes.toString().replaceAll("x:", "").replaceAll("xmlns:x=", "xmlns=")) : bytes);
  assert.deepEqual(readImportCells(plain, "اللاعبون"), readImportCells(source, "اللاعبون"));
  const changed = editImportCells(plain, [{ sheet: "اللاعبون", cell: "D2", value: "0101234567" }]);
  assert.deepEqual(readImportCells(changed, "اللاعبون").get("D2"), { value: "0101234567", isText: true });
});

function serverDates(serial: number, date1904 = false, format = 164) {
  return ownImportWorkbook(source, {}, (name, bytes) => {
    let xml = bytes.toString();
    if (name === "xl/styles.xml") {
      xml = xml.replace(/(<x:styleSheet[^>]*>)/, '$1<x:numFmts count="1"><x:numFmt numFmtId="164" formatCode="yyyy-mm-dd"/></x:numFmts>')
        .replace(/<x:cellXfs count="6">/, '<x:cellXfs count="7">')
        .replace('</x:cellXfs>', `<x:xf numFmtId="${format}"/></x:cellXfs>`);
    }
    if (name === "xl/workbook.xml" && date1904) xml = xml.replace(/(<x:workbook[^>]*>)/, '$1<x:workbookPr date1904="1"/>');
    if (name === "xl/worksheets/sheet2.xml") xml = xml.replace(/<x:c\b[^>]*\br="E2"[^>]*>[\s\S]*?<\/x:c>/, `<x:c r="E2" s="6"><x:v>${serial}</x:v></x:c>`);
    return Buffer.from(xml);
  });
}
test("reads the server's styled numeric date while preserving numeric phone types", () => {
  const cells = readImportCells(serverDates(46296), "اللاعبون");
  assert.deepEqual(cells.get("E2"), { value: "2026-10-01", isText: false });
  const numeric = editImportCells(serverDates(46296), [{ sheet: "اللاعبون", cell: "D2", value: "1012345678", isText: false }]);
  assert.deepEqual(readImportCells(numeric, "اللاعبون").get("D2"), { value: "1012345678", isText: false });
});
test("reads built-in date formats and the workbook's 1904 date epoch", () => {
  assert.equal(readImportCells(serverDates(46296, false, 14), "اللاعبون").get("E2")?.value, "2026-10-01");
  assert.equal(readImportCells(serverDates(44834, true), "اللاعبون").get("E2")?.value, "2026-10-01");
});
test("restores a styled numeric date as the intended textual date without changing another cell", () => {
  const dated = serverDates(46296), before = readImportCells(dated, "اللاعبون");
  const repaired = editImportCells(dated, [{ sheet: "اللاعبون", cell: "E2", value: before.get("E2")!.value }]);
  const after = readImportCells(repaired, "اللاعبون");
  assert.deepEqual(after.get("E2"), { value: "2026-10-01", isText: true });
  for (const [cell, data] of before) if (cell !== "E2") assert.deepEqual(after.get(cell), data);
});
test("edits an omitted blank only in an existing row and header column", () => {
  const omitted = ownImportWorkbook(source, {}, (name, bytes) => name === "xl/worksheets/sheet2.xml" ? Buffer.from(bytes.toString().replace(/<x:c\b[^>]*\br="F24"[^>]*>[\s\S]*?<\/x:c>/, '')) : bytes);
  assert.equal(readImportCells(omitted, "اللاعبون").has("F24"), false);
  const changed = editImportCells(omitted, [{ sheet: "اللاعبون", cell: "F24", value: "" }]);
  const after = readImportCells(changed, "اللاعبون");
  assert.deepEqual(after.get("F24"), { value: "", isText: true });
  for (const [cell, data] of readImportCells(omitted, "اللاعبون")) assert.deepEqual(after.get(cell), data);
  assert.throws(() => editImportCells(omitted, [{ sheet: "اللاعبون", cell: "ZZ24", value: "x" }]), /Missing synthetic cell/);
  assert.throws(() => editImportCells(omitted, [{ sheet: "اللاعبون", cell: "F999", value: "x" }]), /Missing synthetic cell/);
});
