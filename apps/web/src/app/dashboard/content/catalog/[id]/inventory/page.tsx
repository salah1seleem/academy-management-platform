import { ProductInventoryEditor } from "../../../../../../features/product-inventory";
export default async function Page({params}:{params:Promise<{id:string}>}){return <ProductInventoryEditor id={(await params).id}/>;}
