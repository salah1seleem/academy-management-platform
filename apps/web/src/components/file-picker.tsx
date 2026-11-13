"use client";

import {type ComponentProps,useEffect,useRef,useState} from "react";
import {Upload} from "lucide-react";
import styles from "./file-picker.module.css";

/** The real native input remains the form control and opens the OS picker.
 * Only its browser-language caption is replaced with an Arabic presentation. */
export function FilePicker(props:Omit<ComponentProps<"input">,"type">) {
  const [filename,setFilename]=useState("");
  const picker=useRef<HTMLSpanElement>(null);
  useEffect(()=>{
    const form=picker.current?.querySelector("input")?.form;
    const reset=(event:Event)=>queueMicrotask(()=>{if(!event.defaultPrevented)setFilename("");});
    form?.addEventListener("reset",reset);
    return ()=>form?.removeEventListener("reset",reset);
  },[props.form]);
  return <span ref={picker} className={styles.picker} data-disabled={props.disabled||undefined}>
    <span className={styles.action} aria-hidden="true"><Upload size={16}/>اختيار ملف</span>
    <bdi className={styles.filename} aria-hidden="true">{filename||"لم يتم اختيار ملف"}</bdi>
    <input {...props} type="file" onChange={event=>{
      setFilename(Array.from(event.currentTarget.files??[]).map(file=>file.name).join("، "));
      props.onChange?.(event);
    }}/>
  </span>;
}
