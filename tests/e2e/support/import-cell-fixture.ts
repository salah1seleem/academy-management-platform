import { posix } from "node:path";
import { ownImportWorkbook } from "./import-workbook.ts";

export type FixtureCell = { value: string; isText: boolean };
export type FixtureEdit = { sheet: string; cell: string; value: string; isText?: boolean };
const escape = (value: string) => value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
const decode = (value: string) => value.replace(/&(#x[\da-f]+|#\d+|amp|lt|gt|quot|apos);/gi, (_, entity: string) => {
  if (entity.startsWith("#")) return String.fromCodePoint(entity[1].toLowerCase() === "x" ? parseInt(entity.slice(2), 16) : parseInt(entity.slice(1), 10));
  const names: Record<string, string> = { amp: "&", lt: "<", gt: ">", quot: '"', apos: "'" };
  return names[entity.toLowerCase()];
});
const attributes = (value: string) => Object.fromEntries([...value.matchAll(/([\w:]+)="([^"]*)"/g)].map(match => [match[1], decode(match[2])]));
const text = (value = "") => [...value.matchAll(/<(?:[\w.-]+:)?t(?:\s[^>]*)?>([\s\S]*?)<\/(?:[\w.-]+:)?t>/g)].map(match => decode(match[1])).join("");
function parts(input: Buffer) {
  const entries = new Map<string, Buffer>();
  ownImportWorkbook(input, {}, (name, data) => { entries.set(name, data); return data; });
  return entries;
}
function sheetPath(entries: Map<string, Buffer>, name: string) {
  const workbook = entries.get("xl/workbook.xml")?.toString();
  const relationships = entries.get("xl/_rels/workbook.xml.rels")?.toString();
  if (!workbook || !relationships) throw new Error("Missing synthetic workbook metadata");
  const sheet = [...workbook.matchAll(/<(?:[\w.-]+:)?sheet\b([^>]+)\/?\s*>/g)].map(match => attributes(match[1])).find(sheet => sheet.name === name);
  const relationship = [...relationships.matchAll(/<(?:[\w.-]+:)?Relationship\b([^>]+)\/?\s*>/g)].map(match => attributes(match[1])).find(link => link.Id === sheet?.["r:id"]);
  if (!sheet || !relationship) throw new Error(`Missing synthetic sheet ${name}`);
  const path = relationship.Target.startsWith("/") ? relationship.Target.slice(1) : posix.normalize(`xl/${relationship.Target}`);
  if (!path.startsWith("xl/worksheets/") || !entries.has(path)) throw new Error("Invalid synthetic sheet relationship");
  return path;
}

// Only for bounded, server-generated fixtures. Change exact cells without an
// Excel round trip that could repair intentionally invalid types or values.
export function editImportCells(input: Buffer, edits: FixtureEdit[]) {
  const entries = parts(input), changes = new Map<string, FixtureEdit[]>(), seen = new Set<string>();
  for (const edit of edits) {
    if (!/^[A-Z]{1,3}[1-9]\d{0,4}$/.test(edit.cell)) throw new Error("Invalid synthetic cell address");
    const path = sheetPath(entries, edit.sheet), key = `${path}:${edit.cell}`;
    if (seen.has(key)) throw new Error("Ambiguous duplicate synthetic edit");
    seen.add(key); changes.set(path, [...(changes.get(path) ?? []), edit]);
  }
  return ownImportWorkbook(input, {}, (name, data) => {
    let xml = data.toString();
    for (const edit of changes.get(name) ?? []) {
      const pattern = new RegExp(`<((?:[\\w.-]+:)?)c\\b([^>]*\\br="${edit.cell}"[^>]*?)(?:\\/\\s*>|>([\\s\\S]*?)<\\/(?:[\\w.-]+:)?c>)`);
      if (edit.isText === false && !/^-?\d+(?:\.\d+)?$/.test(edit.value)) throw new Error("Invalid synthetic numeric representation");
      const render = (prefix: string, attrs: string) => {
        const kept = attrs.replace(/\bt="[^"]*"\s*/, "").trim();
        return edit.isText === false ? `<${prefix}c ${kept}><${prefix}v>${edit.value}</${prefix}v></${prefix}c>` : `<${prefix}c ${kept} t="inlineStr"><${prefix}is><${prefix}t xml:space="preserve">${escape(edit.value)}</${prefix}t></${prefix}is></${prefix}c>`;
      };
      if (pattern.test(xml)) xml = xml.replace(pattern, (_, prefix: string, attrs: string) => render(prefix, attrs));
      else {
        // The server omits empty cells. Permit an exact blank cell only inside
        // an existing row and an existing header column, never a new record.
        const column = edit.cell.replace(/\d+$/, ""), row = edit.cell.replace(/^[A-Z]+/, "");
        const header = new RegExp(`<(?:[\\w.-]+:)?c\\b[^>]*\\br="${column}1"[\\s>]`);
        const rowPattern = new RegExp(`(<((?:[\\w.-]+:)?)row\\b[^>]*\\br="${row}"[^>]*>)([\\s\\S]*?)(<\\/(?:[\\w.-]+:)?row>)`);
        if (!header.test(xml) || !rowPattern.test(xml)) throw new Error(`Missing synthetic cell ${edit.cell}`);
        xml = xml.replace(rowPattern, (_, start: string, prefix: string, body: string, end: string) => {
          const ordinal = (value: string) => [...value].reduce((total, letter) => total * 26 + letter.charCodeAt(0) - 64, 0);
          const next = [...body.matchAll(/<(?:[\w.-]+:)?c\b[^>]*\br="([A-Z]+)\d+"/g)].find(cell => ordinal(cell[1]) > ordinal(column));
          const at = next?.index ?? body.length;
          return start + body.slice(0, at) + render(prefix, `r="${edit.cell}"`) + body.slice(at) + end;
        });
      }
    }
    return changes.has(name) ? Buffer.from(xml) : data;
  });
}

export function readImportCells(input: Buffer, sheet: string): Map<string, FixtureCell> {
  const entries = parts(input), xml = entries.get(sheetPath(entries, sheet))!.toString();
  const styles = entries.get("xl/styles.xml")?.toString() ?? "";
  const formats = new Map([...styles.matchAll(/<(?:[\w.-]+:)?numFmt\b([^>]+)\/?\s*>/g)].map(match => {
    const attrs = attributes(match[1]); return [Number(attrs.numFmtId), attrs.formatCode];
  }));
  const cellStyles = /<(?:[\w.-]+:)?cellXfs\b[^>]*>([\s\S]*?)<\/(?:[\w.-]+:)?cellXfs>/.exec(styles)?.[1] ?? "";
  const dateStyles = [...cellStyles.matchAll(/<(?:[\w.-]+:)?xf\b([^>]+)>/g)].map(match => {
    const id = Number(attributes(match[1]).numFmtId);
    return [14, 15, 16, 17, 22].includes(id) || /y|d/i.test(formats.get(id) ?? "");
  });
  const date1904 = /\bdate1904="(?:1|true)"/.test(entries.get("xl/workbook.xml")!.toString());
  const strings = [...(entries.get("xl/sharedStrings.xml")?.toString() ?? "").matchAll(/<(?:[\w.-]+:)?si(?:\s[^>]*)?>([\s\S]*?)<\/(?:[\w.-]+:)?si>/g)].map(match => text(match[1]));
  return new Map([...xml.matchAll(/<(?:[\w.-]+:)?c\b([^>]*?)(?:\/\s*>|>([\s\S]*?)<\/(?:[\w.-]+:)?c>)/g)].map(match => {
    const attrs = attributes(match[1]), raw = decode(/<(?:[\w.-]+:)?v(?:\s[^>]*)?>([\s\S]*?)<\/(?:[\w.-]+:)?v>/.exec(match[2] ?? "")?.[1] ?? "");
    let value = attrs.t === "s" ? strings[Number(raw)] : attrs.t === "inlineStr" ? text(match[2]) : raw;
    if (value === undefined) throw new Error("Invalid synthetic string reference");
    if (!["s", "str", "inlineStr"].includes(attrs.t) && raw && dateStyles[Number(attrs.s ?? 0)]) {
      const serial = Number(raw);
      if (!Number.isFinite(serial)) throw new Error("Invalid synthetic date serial");
      const epoch = Date.UTC(date1904 ? 1904 : 1899, date1904 ? 0 : 11, date1904 ? 1 : 30);
      value = new Date(epoch + serial * 86400000).toISOString().slice(0, 10);
    }
    return [attrs.r, { value, isText: ["s", "str", "inlineStr"].includes(attrs.t) }];
  }));
}
