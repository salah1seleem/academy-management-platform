import { ProductSaleForm } from "../../../../../../features/product-sale-form";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <ProductSaleForm id={id} />; }
