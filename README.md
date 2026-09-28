# Academy Management Platform

منصة مستقلة، عربية أولاً، لإدارة أكاديميات متعددة من كود واحد مع عزل بيانات كل أكاديمية. مالك المتطلبات: صلاح.

## مداخل التوثيق

- المتطلبات الحاكمة: [`docs/requirements/CURRENT_REQUIREMENTS.md`](docs/requirements/CURRENT_REQUIREMENTS.md)
- اعتماد المالك: [`docs/requirements/APPROVAL_2026-09-28.md`](docs/requirements/APPROVAL_2026-09-28.md)
- المعمارية المقترحة: [`docs/architecture/ARCHITECTURE_v1.0.md`](docs/architecture/ARCHITECTURE_v1.0.md)
- نموذج المجال: [`docs/architecture/DOMAIN_MODEL_v1.0.md`](docs/architecture/DOMAIN_MODEL_v1.0.md)
- القرارات المفتوحة: [`docs/architecture/DECISIONS_v1.0.md`](docs/architecture/DECISIONS_v1.0.md)
- التتبّع: [`docs/architecture/REQUIREMENTS_MAPPING_v1.0.md`](docs/architecture/REQUIREMENTS_MAPPING_v1.0.md)
- خطة التنفيذ: [`docs/delivery/IMPLEMENTATION_PLAN_v1.0.md`](docs/delivery/IMPLEMENTATION_PLAN_v1.0.md)
- الحالة الفعلية: [`docs/delivery/STATUS.md`](docs/delivery/STATUS.md)

## الهيكل المقصود

```text
apps/api/          ASP.NET Core Web API modular monolith
apps/web/          Next.js Arabic-first PWA
tests/             unit, integration, architecture, end-to-end
infra/local/       Docker Compose للتطوير فقط
docs/              requirements, architecture, delivery
```

## الحالة الحالية

المستودع يحتوي متطلبات معتمدة ووثائق معمارية مقترحة للمراجعة فقط. لا يوجد حتى الآن تطبيق، أو migration، أو قاعدة بيانات منشأة، أو seed منفذ، أو demo يعمل، أو نشر. لذلك لا توجد أوامر تشغيل تطبيق موثوقة في هذا الملف.
