# Business activity profiles

## Purpose and catalog

Business activity profiles customize recommended capabilities without licensing or restricting any capability. Administrators may select multiple stable keys and exactly one selected key as primary. The central Application registry currently contains:

| Key | Arabic name | Suggested capability keys |
| --- | --- | --- |
| `general-retail` | تجارة التجزئة العامة | `inventory`, `sales`, `barcode-scanning` |
| `grocery` | سوبر ماركت وبقالة | `expiry-dates`, `batch-tracking`, `multiple-units`, `barcode-scanning` |
| `clothing` | ملابس | `sizes`, `colors`, `product-variants`, `barcode-labels` |
| `footwear` | أحذية | `sizes`, `colors`, `product-variants`, `barcode-labels` |
| `mobile-phones` | هواتف محمولة | `serial-numbers`, `imei-tracking`, `warranty-tracking`, `maintenance-management` |
| `electronics` | إلكترونيات | `serial-numbers`, `warranty-tracking`, `maintenance-management` |
| `accessories` | إكسسوارات | `product-variants`, `barcode-labels`, `barcode-scanning` |
| `cosmetics` | مستحضرات تجميل | `expiry-dates`, `batch-tracking`, `product-variants` |
| `pharmacy-retail` | تجزئة مرتبطة بالصيدليات | `expiry-dates`, `batch-tracking`, `barcode-scanning` |
| `hardware-electrical` | أدوات وكهرباء | `multiple-units`, `serial-numbers`, `warranty-tracking` |
| `wholesale` | تجارة الجملة | `multiple-units`, `batch-tracking`, `wholesale-pricing` |
| `maintenance-repair` | صيانة وإصلاح | `maintenance-management`, `serial-numbers`, `warranty-tracking` |
| `other` | نشاط آخر أو مخصص | none by default |

The pharmacy-related entry is only a retail recommendation profile; it does not claim or implement regulated pharmacy workflows.

## Recommendation behavior

The Application service accepts stable activity keys, validates them against active registry entries, groups capability suggestions case-insensitively, and returns one recommendation per capability. Each result contains `suggestedEnabled: true`, an Arabic reason, and every source activity key. Ordering is deterministic.

Comparison reads capability configuration without changing it. It reports newly recommended keys, stored recommendations no longer produced by the proposed activities, and administrator overrides. Applying recommendations is a separate confirmed choice. It only inserts or re-enables `recommended` rows; the SQLite conflict condition never updates an `administrator` row. Removing an activity changes selection metadata only and never deletes business records or disables capabilities.

## Persistence and API

Migration `004_business_activities.sql` creates `BusinessActivities` and `CapabilityConfiguration`; both use the configured database path and UTC ISO timestamps. The selection save is transactional and the partial unique index permits at most one primary activity. See `docs/database.md` for columns and `docs/api-contracts.md` for the four authenticated endpoints.

## UI and first run

After real login the shell reads `/api/business-activities/selected`. If it is not configured, the shell opens `business-activities`; it never changes the login flow. The Arabic RTL screen searches the server catalog, supports multi-select, requires a primary selection, previews recommendation deltas, and confirms before a real PUT. Administrators can edit; other authenticated roles can inspect only. Once saved, the same screen remains available under `الإعدادات ← نوع النشاط التجاري`.

## Task 2 integration contract

Task 2 consumes `CapabilityRecommendation` without changing activity selection. Explicit application resolves each stable key through the feature registry: `maintenance-management` is an optional module, product keys remain capabilities, and `barcode-labels` also recommends `barcode-label-printing`. Dependency closure is included, `administrator` rows are preserved, and activity removal remains advisory rather than destructive.
