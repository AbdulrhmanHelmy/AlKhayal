# Module and capability catalog

`FeatureRegistry` is the canonical catalog. Keys are stable identifiers. Core modules are always enabled and cannot be changed. Optional modules default disabled until an administrator or confirmed activity recommendation enables them.

## Core modules

`authentication`, `basic-settings`, `user-administration`, `product-catalog`, `basic-inventory`, `sales`, `purchases`, `customers`, `suppliers`, and `core-finance`.

## Optional modules

| Key | Arabic name | Default |
| --- | --- | --- |
| `maintenance-management` | إدارة الصيانة | Off |
| `installments` | البيع بالتقسيط | Off |
| `manufacturing` | التصنيع | Off |
| `multiple-warehouses` | المخازن المتعددة | Off |
| `multiple-branches` | الفروع المتعددة | Off |
| `employee-attendance` | حضور الموظفين | Off |
| `advanced-shifts` | إدارة الورديات المتقدمة | Off |
| `cash-services` | خدمات الكاش | Off |
| `advanced-reports` | التقارير المتقدمة | Off |
| `barcode-label-printing` | طباعة ملصقات الباركود | Off |

## Product capabilities

`sizes`, `colors`, `product-variants`, `serial-numbers`, `imei-tracking`, `warranty-tracking`, `expiry-dates`, `batch-tracking`, `multiple-units`, `weight-based-products`, `product-barcode-settings`, `barcode-labels`, `barcode-scanning`, `wholesale-pricing`, `inventory`, and `sales`.

`inventory`, `sales`, and `barcode-scanning` are core/default-enabled capabilities. Other capabilities default off. `product-variants` depends on `sizes` and `colors`; `imei-tracking` depends on `serial-numbers`. Missing dependencies and enabled dependents block invalid changes.

Capabilities are global availability, not mandatory fields on every product. `ProductCapabilityApplicability` distinguishes `globally-disabled`, `enabled-not-applicable`, and `enabled-and-applicable`; a later product phase will persist the per-product choice.

