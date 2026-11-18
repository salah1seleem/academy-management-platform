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
