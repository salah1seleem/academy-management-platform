import { Fragment } from "react";
import styles from "./academy-import.module.css";

type Field = { key: string; label: string; required: boolean };
type Sheet = { name: string; optional: boolean; columns: Field[] };
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === "object" && !Array.isArray(value);
function valid(value: unknown): value is Sheet[] {
  return Array.isArray(value) && value.every(sheet => object(sheet) && typeof sheet.name === "string" && sheet.name.trim().length > 0
    && typeof sheet.optional === "boolean" && Array.isArray(sheet.columns) && sheet.columns.every(field => object(field)
      && typeof field.key === "string" && typeof field.label === "string" && typeof field.required === "boolean"));
}
export function ImportFieldLabel({ label }: { label: string }) {
  return label.split(/(yyyy-MM-dd|yyyy-MM|HH:mm)/).map((part, index) => /^(yyyy-MM-dd|yyyy-MM|HH:mm)$/.test(part)
    ? <bdi key={index} dir="ltr">{part}</bdi> : <Fragment key={index}>{part}</Fragment>);
}
function Fields({ title, fields }: { title: string; fields: Field[] }) {
  if (!fields.length) return null;
  return <div className={styles.schemaFields}>
    <h4>{title} <span>({fields.length})</span></h4>
    <ul>{fields.map(field => <li key={field.key}><ImportFieldLabel label={field.label} /></li>)}</ul>
  </div>;
}
export function ImportSchemaGuide({ sheets }: { sheets: unknown }) {
  return <details className={styles.schema} aria-label="دليل حقول ملف الأكاديمية">
    <summary>الحقول المطلوبة والاختيارية</summary>
    {valid(sheets) ? <>
      <p className={styles.schemaNote}>اكتب أرقام الموبايل في خلايا نصية لحفظ الصفر الأول.</p>
      {sheets.map(sheet => {
        const fields = sheet.columns.filter(field => field.key !== "ExistingId");
        return <details key={sheet.name} className={styles.schemaSheet} open={sheet.name === "اللاعبون"}>
          <summary>{sheet.name}<small>{sheet.optional ? "اختياري بحسب بياناتك" : "أساسي عند الربط"}</small></summary>
          {fields.length ? <div className={styles.schemaColumns}>
            <Fields title="مطلوب" fields={fields.filter(field => field.required)} />
            <Fields title="اختياري" fields={fields.filter(field => !field.required)} />
          </div> : <p className={styles.schemaNote}>تعليمات وأمثلة الأكواد</p>}
        </details>;
      })}
    </> : <p role="alert">تعذرت قراءة دليل الحقول. راجع عناوين القالب، ثم أعد فتح الصفحة.</p>}
  </details>;
}
