"use client";

import {type ComponentProps,useState} from "react";
import {Upload} from "lucide-react";
import styles from "./file-picker.module.css";

/** The real native input remains the form control and opens the OS picker.
 * Only its browser-language caption is replaced with an Arabic presentation. */
export function FilePicker(props:Omit<ComponentProps<"input">,"type">) {
  const [filename,setFilename]=useState("");
  return <span className={styles.picker} data-disabled={props.disabled||undefined}>
    <span className={styles.action} aria-hidden="true"><Upload size={16}/>اختيار ملف</span>
    <span className={styles.filename} aria-hidden="true">{filename||"لم يتم اختيار ملف"}</span>
    <input {...props} type="file" onChange={event=>{
      setFilename(Array.from(event.currentTarget.files??[]).map(file=>file.name).join("، "));
      props.onChange?.(event);
    }}/>
  </span>;
}
