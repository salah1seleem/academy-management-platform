import { inflateRawSync } from "node:zlib";

// Test-only adapter for the server-generated, bounded XLSX fixture. Replaces
// exact shared-string values so a rehearsal owns its groups/source/phone;
// schema, reference stamp and historical financial data remain unchanged.
// No spreadsheet library, formulas, application endpoint or fixture reset.
export function ownImportWorkbook(input: Buffer, replacements: Record<string, string>): Buffer {
  const end = input.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  if (end < 0) throw new Error("Missing synthetic ZIP directory");
  let directory = input.readUInt32LE(end + 16), offset = 0;
  const entries: Buffer[] = [], indexes: Buffer[] = [];
  const xml = (s: string) => s.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
  for (let i = 0; i < input.readUInt16LE(end + 10); i++) {
    if (input.readUInt32LE(directory) !== 0x02014b50) throw new Error("Invalid synthetic directory");
    const length = input.readUInt16LE(directory + 28), extra = input.readUInt16LE(directory + 30), comment = input.readUInt16LE(directory + 32);
    const name = input.subarray(directory + 46, directory + 46 + length), local = input.readUInt32LE(directory + 42);
    const start = local + 30 + input.readUInt16LE(local + 26) + input.readUInt16LE(local + 28);
    const compressed = input.subarray(start, start + input.readUInt32LE(directory + 20));
    const method = input.readUInt16LE(directory + 10);
    if (method !== 0 && method !== 8) throw new Error("Unsupported synthetic compression");
    let data: Buffer = method === 8 ? inflateRawSync(compressed) : compressed;
    if (name.toString() === "xl/sharedStrings.xml") {
      let content = data.toString();
      for (const [from, to] of Object.entries(replacements)) content = content.replaceAll(`>${xml(from)}</t>`, `>${xml(to)}</t>`);
      data = Buffer.from(content);
    }
    let crc = 0xffffffff;
    for (const byte of data) { crc ^= byte; for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0); }
    crc = (crc ^ 0xffffffff) >>> 0;
    const header = Buffer.alloc(30); header.writeUInt32LE(0x04034b50); header.writeUInt16LE(20, 4); header.writeUInt32LE(crc, 14); header.writeUInt32LE(data.length, 18); header.writeUInt32LE(data.length, 22); header.writeUInt16LE(name.length, 26);
    const index = Buffer.alloc(46); index.writeUInt32LE(0x02014b50); index.writeUInt16LE(20, 4); index.writeUInt16LE(20, 6); index.writeUInt32LE(crc, 16); index.writeUInt32LE(data.length, 20); index.writeUInt32LE(data.length, 24); index.writeUInt16LE(name.length, 28); index.writeUInt32LE(offset, 42);
    entries.push(header, name, data); indexes.push(index, name); offset += header.length + name.length + data.length;
    directory += 46 + length + extra + comment;
  }
  const central = Buffer.concat(indexes), footer = Buffer.alloc(22); footer.writeUInt32LE(0x06054b50); footer.writeUInt16LE(indexes.length / 2, 8); footer.writeUInt16LE(indexes.length / 2, 10); footer.writeUInt32LE(central.length, 12); footer.writeUInt32LE(offset, 16);
  return Buffer.concat([...entries, central, footer]);
}
