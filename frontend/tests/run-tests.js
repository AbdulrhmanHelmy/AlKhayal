"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");
var root = path.resolve(__dirname, "..", ".."), calculations = require(path.join(root, "frontend", "js", "core", "calculations.js"));
var registry = JSON.parse(fs.readFileSync(path.join(root, "frontend", "data", "screens.json"), "utf8"));
var indexHtml = fs.readFileSync(path.join(root, "frontend", "index.html"), "utf8");
var layoutCss = fs.readFileSync(path.join(root, "frontend", "css", "layout.css"), "utf8");
var tokensCss = fs.readFileSync(path.join(root, "frontend", "css", "tokens.css"), "utf8");
var themesCss = fs.readFileSync(path.join(root, "frontend", "css", "themes.css"), "utf8");
var modulesCss = fs.readFileSync(path.join(root, "frontend", "css", "modules.css"), "utf8");
var appJs = fs.readFileSync(path.join(root, "frontend", "js", "app.js"), "utf8");
var activityJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "business-activities.js"), "utf8");
var featureJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "feature-management.js"), "utf8");
var featureStateJs = fs.readFileSync(path.join(root, "frontend", "js", "services", "feature-state.js"), "utf8");
var accessStateJs = fs.readFileSync(path.join(root, "frontend", "js", "services", "access-state.js"), "utf8");
var userManagementJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "user-management.js"), "utf8");
var dashboardJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "dashboard.js"), "utf8");
var renderersJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "renderers.js"), "utf8");
var catalogJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "catalog.js"), "utf8");
var inventoryJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "inventory.js"), "utf8");
var uiJs = fs.readFileSync(path.join(root, "frontend", "js", "components", "ui.js"), "utf8");
var settingsHubJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "settings-hub.js"), "utf8");
var generalSettingsJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "general-settings.js"), "utf8");
var settingsStateJs = fs.readFileSync(path.join(root, "frontend", "js", "services", "settings-state.js"), "utf8");
var posJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "pos.js"), "utf8");
var purchasingJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "purchasing.js"), "utf8");
var loginHtml = fs.readFileSync(path.join(root, "frontend", "login.html"), "utf8");
var loginJs = fs.readFileSync(path.join(root, "frontend", "js", "login.js"), "utf8");
var mainFormCs = fs.readFileSync(path.join(root, "src", "AlKhayal.UI", "MainForm.cs"), "utf8");
var serverHostCs = fs.readFileSync(path.join(root, "src", "AlKhayal.Server.Host", "AlKhayalServer.cs"), "utf8");
var allowedRenderers = new Set(["dashboard", "settings", "settings-hub", "legacy-redirect", "under-construction", "business-activities", "feature-management", "user-management", "company-settings", "general-settings", "appearance-settings", "database-settings", "catalog-products", "catalog-product-form", "catalog-categories", "catalog-units", "warehouse-directory", "entity-list", "entity-form", "statement", "payment", "export", "report", "pos", "invoice-lookup", "workflow", "invoice-editor", "picker", "returns", "printer", "barcode", "expense-form", "alerts", "threshold", "adjustment", "transfer", "manufacturing", "attendance", "permissions", "installments", "maintenance-intake", "maintenance-tracking", "cash-services", "purchase-management", "purchase-list", "purchase-returns"]);

assert.ok(loginHtml.indexOf("مرحبًا بك في الخيال. يجب إنشاء حساب المدير للبدء.") >= 0, "login must contain the first-run Arabic message");
assert.ok(loginHtml.indexOf("إنشاء حساب المدير") >= 0, "login must expose the first Admin action");
assert.ok(loginJs.indexOf("/api/auth/bootstrap-status") >= 0 && loginJs.indexOf("/api/auth/bootstrap") >= 0, "login must use the bootstrap API contract");

var routes = registry.screens.map(function (screen) { return screen.route; });
assert.strictEqual(new Set(routes).size, routes.length, "screen routes must be unique");
assert.ok(routes.length >= 90, "the full inventory should register at least 90 distinct screens");
registry.modules.forEach(function (module) { module.items.forEach(function (route) { assert.ok(routes.indexOf(route) >= 0, "navigation route is missing: " + route); }); });
var sidebarRoutes = registry.modules.reduce(function(all,module){return all.concat(module.items);},[]);
assert.strictEqual(new Set(sidebarRoutes).size,sidebarRoutes.length,"main sidebar routes must not be duplicated");
assert.deepStrictEqual(registry.modules.find(function(module){return module.id==="inventory";}).items,["products-list","warehouses-list"],"inventory sidebar must contain only the product and warehouse directories");
assert.ok(indexHtml.indexOf('js/modules/inventory.js')>=0&&inventoryJs.indexOf('R["warehouse-directory"]=directory')>=0,"real warehouse renderer must load after catalog module");
assert.ok(indexHtml.indexOf('js/modules/purchasing.js')>=0&&purchasingJs.indexOf('global.AlKhayalRenderers["purchase-management"]')>=0&&purchasingJs.indexOf('global.AlKhayalRenderers["purchase-returns"]')>=0,"purchasing and traceable return screens must load real renderers");
assert.ok(purchasingJs.indexOf('"imei-tracking"')>=0&&purchasingJs.indexOf('returnMode: state.mode')>=0&&purchasingJs.indexOf('لا توجد حركة خزينة فعلية')>=0,"purchase UI must collect tracking identities and clearly distinguish refund recording from treasury posting");
assert.ok(inventoryJs.indexOf('/api/inventory/post')>=0&&inventoryJs.indexOf('/api/inventory/counts')>=0&&inventoryJs.indexOf('/api/inventory/movements')>=0,"inventory views must use persisted APIs");
assert.ok(registry.screens.filter(function(screen){return ["stock-adjustment","warehouse-transfer","stock-movements"].indexOf(screen.route)>=0;}).every(function(screen){return screen.redirectTo==="warehouses-list";}),"legacy stock routes must resolve through the warehouse directory");
registry.quickAccess.forEach(function (item) { assert.ok(routes.indexOf(item.route) >= 0, "quick-access route is missing: " + item.route); });
registry.screens.forEach(function (screen) { assert.ok(allowedRenderers.has(screen.renderer), "unknown renderer: " + screen.renderer); assert.ok(screen.title && screen.module && screen.description, "screen metadata is incomplete: " + screen.route); });
assert.ok(indexHtml.indexOf('id="workspace"') >= 0 && indexHtml.indexOf('id="sidebarNavigation"') >= 0, "shell scroll regions must remain addressable");
assert.ok(layoutCss.indexOf("min-height: 0") >= 0 && layoutCss.indexOf("overflow-y: auto") >= 0, "shell must define a bounded vertical content scroller");
assert.ok(layoutCss.indexOf("grid-template-columns: minmax(0, 1fr) var(--sidebar-width)") >= 0, "shell must use intrinsic grid sizing");
assert.ok(modulesCss.indexOf("height: calc(100vh") < 0, "module layouts must not own a second viewport height");
assert.ok(indexHtml.indexOf("toggle-icon-close") >= 0 && indexHtml.indexOf("toggle-icon-open") >= 0, "sidebar toggle must expose directional panel icons");
assert.ok(indexHtml.indexOf("طي القائمة الجانبية") >= 0 && appJs.indexOf("توسيع القائمة الجانبية") >= 0, "sidebar toggle labels must be Arabic and state-specific");
assert.ok(appJs.indexOf("iconPaths") >= 0 && appJs.indexOf("settings:") >= 0 && appJs.indexOf("suppliers:") >= 0, "navigation icons must use the local semantic SVG map");
assert.ok(layoutCss.indexOf("gap: 0; padding-inline: 0") >= 0, "collapsed rail controls must remove expanded spacing");
assert.ok(tokensCss.indexOf("--sidebar-bg")>=0&&themesCss.indexOf("--sidebar-bg")>=0&&layoutCss.indexOf("var(--sidebar-active)")>=0,"sidebar colors must use distinct light/dark semantic tokens for active and hover states");
assert.ok(routes.indexOf("business-activities") >= 0 && indexHtml.indexOf("business-activities.js") >= 0, "business activity settings must be registered and loaded");
assert.ok(indexHtml.indexOf('lang="ar" dir="rtl"') >= 0, "business activity setup must inherit Arabic RTL shell semantics");
assert.ok(modulesCss.indexOf(".activity-layout") >= 0 && modulesCss.indexOf("@media (max-width: 760px)") >= 0, "business activity layout must support compact viewports and both token-driven themes");
assert.ok(appJs.indexOf('!selection.isConfigured&&routeEnabled(screensByRoute["business-activities"])') >= 0, "first-run must open activity setup only when the administrator can access it");
assert.ok(activityJs.indexOf('type="checkbox"') >= 0 && activityJs.indexOf("selected.push") >= 0, "activity UI must support multi-selection");
assert.ok(activityJs.indexOf('id="primaryActivity"') >= 0 && activityJs.indexOf("primary = this.value") >= 0, "activity UI must support primary selection");
assert.ok(activityJs.indexOf("/api/business-activities/recommendations") >= 0 && activityJs.indexOf("newlyRecommended") >= 0 && activityJs.indexOf("noLongerRecommended") >= 0, "activity UI must preview recommendation changes");
assert.ok(activityJs.indexOf("UI.confirm") >= 0 && activityJs.indexOf('method: "PUT"') >= 0, "activity saving must require confirmation and use the real API");
assert.ok(activityJs.indexOf('UI.toast("تم حفظ نوع النشاط التجاري') >= 0 && activityJs.indexOf('UI.toast(reason.message, "danger")') >= 0, "activity UI must expose save success and failure states");
assert.ok(activityJs.indexOf('role === "administrator"') >= 0 && activityJs.indexOf("للعرض فقط") >= 0, "cashier activity controls must be read-only");
assert.ok(routes.indexOf("feature-management") >= 0 && indexHtml.indexOf("feature-management.js") >= 0 && indexHtml.indexOf("feature-state.js") >= 0, "feature management must be registered and loaded");
assert.ok(featureJs.indexOf('"/api/features/"') >= 0 && featureJs.indexOf('kind === "module" ? "modules/" : "capabilities/"') >= 0, "feature switches must persist through the real API");
assert.ok(featureJs.indexOf("UI.confirm") >= 0 && featureJs.indexOf("reason.message") >= 0, "feature changes must confirm and expose API errors");
assert.ok(featureJs.indexOf('role === "administrator"') >= 0 && featureJs.indexOf("للعرض فقط") >= 0, "cashiers must receive read-only feature management");
assert.ok(featureStateJs.indexOf("/api/features/effective") >= 0 && appJs.indexOf("Features.routeEnabled") >= 0, "navigation must use the effective server snapshot");
assert.ok(appJs.indexOf("لا تملك صلاحية فتح هذه الشاشة أو أن وحدتها معطلة") >= 0 && appJs.indexOf("reconcileAccess") >= 0, "direct routes and already-open tabs must react to disabled modules or revoked access");
assert.ok(featureJs.indexOf("Features.load({ notify: false })") >= 0, "the feature screen must refresh state without recursively notifying the shell");
assert.strictEqual((appJs.match(/alkhayal:features-changed/g) || []).length, 1, "the shell must register exactly one feature-state listener");
assert.ok(appJs.indexOf("if(removedActive||!activeRoute)renderActive();") >= 0, "feature and access changes must not recreate an unaffected active screen");
assert.ok(appJs.indexOf("return !screen.navigationHidden && routeEnabled(screen)") >= 0, "global navigation search must exclude hidden, disabled, and unauthorized routes");
assert.ok(registry.screens.filter(function (screen) { return screen.featureKey; }).length >= 20, "optional module routes must carry stable feature metadata");
assert.ok(modulesCss.indexOf(".feature-grid") >= 0 && modulesCss.indexOf("@media (max-width: 760px)") >= 0, "feature settings must support compact token-driven layouts");
assert.ok(routes.indexOf("users-list") >= 0 && indexHtml.indexOf("user-management.js") >= 0 && indexHtml.indexOf("access-state.js") >= 0, "real user management and access state must be registered");
assert.ok(registry.screens.filter(function(screen){return screen.administratorOnly;}).every(function(screen){return screen.module === "الإعدادات" || screen.module === "صلاحيات المستخدم";}), "administrator-only metadata must be explicit in the canonical registry");
assert.ok(userManagementJs.indexOf('"/api/users"') >= 0 && userManagementJs.indexOf("/screen-permissions") >= 0 && userManagementJs.indexOf("/reset-password") >= 0, "user management must use real account APIs");
assert.ok(userManagementJs.indexOf("data-role-filter") >= 0 && userManagementJs.indexOf("data-status-filter") >= 0 && userManagementJs.indexOf("data-permission-search") >= 0, "user list and permission editor must provide search and filters");
assert.ok(userManagementJs.indexOf("تغييرات غير محفوظة") >= 0 && userManagementJs.indexOf("data-select-all") >= 0 && userManagementJs.indexOf("data-clear-all") >= 0, "permission editor must warn and support bulk selection");
assert.ok(dashboardJs.indexOf("context.canNavigate") >= 0 && appJs.indexOf("!routeEnabled(screen)") >= 0, "dashboard shortcuts and direct navigation must enforce effective access");
assert.ok(dashboardJs.indexOf("S.dashboard")<0&&dashboardJs.indexOf("latestSales")<0&&dashboardJs.indexOf("مبيعات اليوم")<0,"dashboard must not present fabricated figures, dates, or activity");
assert.ok(appJs.indexOf("implementedRoutes")>=0&&appJs.indexOf('renderers["under-construction"]')>=0&&renderersJs.indexOf("هذه الشاشة قيد الإنشاء")>=0,"unfinished routes must use the uniform non-mutating construction state");
assert.ok(featureJs.indexOf("muted ltr")<0,"feature cards must not expose internal feature keys to staff");
assert.strictEqual((appJs.match(/alkhayal:access-changed/g) || []).length, 1, "the shell must register exactly one access-state listener");
assert.ok(indexHtml.indexOf("settings-hub.js")>=0&&settingsHubJs.indexOf("settingsGroup")>=0,"the Settings hub must load and derive entries from registry metadata");
assert.ok(indexHtml.indexOf("general-settings.js")>=0&&indexHtml.indexOf("settings-state.js")>=0,"real settings UI and shared formatter state must load in the shell");
assert.ok(settingsStateJs.indexOf('minimumFractionDigits: 2, maximumFractionDigits: 2')>=0,"money formatting must remain fixed at two decimal digits");
assert.ok(generalSettingsJs.indexOf("options:['2']")>=0,"general settings must not offer non-piastre display precision");
var realSettings={"settings-general":"general-settings","settings-company":"company-settings","settings-appearance":"appearance-settings","settings-backup":"database-settings"};
Object.keys(realSettings).forEach(function(route){var screen=registry.screens.find(function(item){return item.route===route;});assert.strictEqual(screen.renderer,realSettings[route],route+" must use a real renderer");assert.ok(screen.settingsGroup,"real settings routes must remain in the registry-driven Settings hub");});
assert.ok(generalSettingsJs.indexOf("/api/settings/company")>=0&&generalSettingsJs.indexOf("/api/settings/general")>=0&&generalSettingsJs.indexOf("/api/settings/database-info")>=0,"settings screens must use the authenticated API");
assert.ok(generalSettingsJs.indexOf("إلغاء التغييرات")>=0&&generalSettingsJs.indexOf("context.isActive()")>=0,"settings forms must support cancel and stale-response protection");
assert.ok(generalSettingsJs.indexOf("context.state.companyDraft")>=0&&generalSettingsJs.indexOf("context.state.generalDraft")>=0,"unsaved settings drafts must survive workspace tab switches");
assert.ok(generalSettingsJs.indexOf("AlKhayalTheme.set")>=0&&generalSettingsJs.indexOf("light")>=0&&generalSettingsJs.indexOf("dark")>=0&&generalSettingsJs.indexOf("system")>=0,"appearance must reuse all three existing theme modes");
assert.ok(indexHtml.indexOf("catalog.js")>=0&&catalogJs.indexOf("/api/catalog/products")>=0,"the real catalog module must load and use the catalog API");
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="products-list";}).renderer,"catalog-products","product list must use the real catalog renderer");
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="product-add";}).renderer,"catalog-product-form","product creation must use the real catalog renderer");
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="product-edit";}).renderer,"catalog-product-form","product editing must use the real catalog renderer");
assert.ok(catalogJs.indexOf('R["warehouse-directory"]')<0&&inventoryJs.indexOf('R["warehouse-directory"]=directory')>=0,"Warehouse Directory must have one real renderer");
assert.ok(catalogJs.indexOf("/api/catalog/attributes")>=0&&catalogJs.indexOf("/api/catalog/barcodes/lookup")>=0,"Task 2.2 catalog configuration endpoints must be wired");
assert.ok(catalogJs.indexOf("data-capability")>=0&&catalogJs.indexOf("data-unit-num")>=0&&catalogJs.indexOf("data-variant-id")>=0,"product detail must expose capabilities, exact unit conversions, and variants");
assert.ok(catalogJs.indexOf("data-capability-toggle")>=0&&catalogJs.indexOf("catalog-capability-switch")>=0,"product capabilities must use accessible Arabic toggle switches");
assert.ok(catalogJs.indexOf('var capabilityOrder=["expiry-dates","colors","sizes"]')>=0&&catalogJs.indexOf('isGloballyEnabled:true')>=0&&catalogJs.indexOf("item.isAssigned?' checked'")>=0,"the three simplified product choices must always be available while saved assignments remain selected");
assert.ok(catalogJs.indexOf('data-free-values="')>=0&&catalogJs.indexOf('data-add-free-value')>=0&&catalogJs.indexOf('area.querySelector("[data-free-value-list]").appendChild(row)')>=0,"sizes and colors must use inline editable rows without rerendering the form");
assert.ok(catalogJs.indexOf('unitRows=[{id:base?base.id:null,unitId:baseId,numerator:1,denominator:1,isBase:true')>=0,"the selected base unit must produce a canonical 1/1 mapping without enabling multiple units");
assert.ok(catalogJs.indexOf('data-generate-barcode')>=0&&catalogJs.indexOf('إنشاء باركود')>=0&&catalogJs.indexOf('barcodeChecksum')>=0&&catalogJs.indexOf('duplicate_barcode')<0,"barcode input must be always available with an Arabic generation control while uniqueness stays server-authoritative");
assert.ok(catalogJs.indexOf('state.drafts')>=0&&catalogJs.indexOf('restoreProductDraft')>=0&&catalogJs.indexOf('catalog-form-actions')>=0,"product drafts and one accessible save footer must survive tab renders");
assert.ok(catalogJs.indexOf('var capabilityOrder=["expiry-dates","colors","sizes"]')>=0&&catalogJs.indexOf('data-capability-toggle="batch-tracking"')<0&&catalogJs.indexOf('data-capability-toggle="serial-numbers"')<0&&catalogJs.indexOf('data-capability-toggle="imei-tracking"')<0&&catalogJs.indexOf('catalogType')<0,"new-product UI must expose only expiry, colors, and sizes without a product-type or legacy tracking control");
assert.ok(catalogJs.indexOf('record.capabilities||[]')>=0&&catalogJs.indexOf('filter(function(unit){return !unit.isBase;})')>=0,"edits must carry forward hidden legacy capabilities and non-base units instead of silently overwriting them");
assert.ok(featureJs.indexOf('section("خصائص المنتجات"')<0&&featureJs.indexOf('snapshot.capabilities')<0,"global product-capability controls must be removed from feature management");
assert.ok(modulesCss.indexOf(".catalog-switch-track")>=0&&modulesCss.indexOf("focus-within")>=0,"catalog switches must have visible keyboard focus and theme-aware styling");
assert.ok(catalogJs.indexOf("context.isActive()")>=0&&catalogJs.indexOf("data-catalog-retry")>=0,"catalog renderers must protect stale responses and expose retry states");
assert.ok(catalogJs.indexOf("سعر الشراء")>=0&&catalogJs.indexOf("purchasePrice")>=0&&catalogJs.indexOf("ليست متوسط تكلفة المخزون")>=0,"product forms must capture the optional reference purchase cost honestly");
assert.ok(catalogJs.indexOf("data-add-category-inline")>=0&&catalogJs.indexOf("categories.push(saved)")>=0&&catalogJs.indexOf("select.value=String(saved.id)")>=0,"inline category creation must preserve the mounted draft, refresh options, and select the new category");
assert.ok(catalogJs.indexOf("data-manage-categories")>=0&&catalogJs.indexOf('context.navigate("product-categories")')>=0&&catalogJs.indexOf('context.navigate("unit-add")')>=0,"catalog must expose category and unit management contextually");
assert.ok(catalogJs.indexOf("compactCategories")>=0&&catalogJs.indexOf("compactUnits")>=0&&catalogJs.indexOf('R["catalog-categories"]=compactCategories')>=0&&catalogJs.indexOf('R["catalog-units"]=compactUnits')>=0,"category and unit routes must use table-first renderers");
assert.ok(catalogJs.indexOf("catalogManagementModal")>=0&&catalogJs.indexOf("+ إضافة تصنيف")>=0&&catalogJs.indexOf("+ إضافة وحدة قياس")>=0,"catalog management must expose compact modal add actions");
assert.ok(catalogJs.indexOf("جارٍ الحفظ")>=0&&catalogJs.indexOf("تغييرات غير محفوظة")>=0&&catalogJs.indexOf("data-toggle-management")>=0,"management modals must validate, prevent duplicate saves, and protect unsaved edits");
assert.ok(uiJs.indexOf("beforeClose")>=0&&uiJs.indexOf("focusSelector")>=0&&uiJs.indexOf("event.key === \"Escape\"")>=0,"shared modal behavior must support close guards, initial focus, Escape, and focus trapping");
var inventoryModule=registry.modules.find(function(module){return module.id==="inventory";});
var customerModule=registry.modules.find(function(module){return module.id==="customers";});
assert.deepStrictEqual(Array.from(customerModule.items),["customers-list","customer-statement","customer-credit-payment","customers-export"],"customer sidebar must keep the management, statement, credit-payment, and export entries only");
["customers-list","customer-statement","customer-credit-payment"].forEach(function(route){assert.ok(customerModule.items.indexOf(route)>=0,"customer sidebar must keep "+route);});
["customer-add","customer-edit","customer-subscriptions"].forEach(function(route){assert.ok(customerModule.items.indexOf(route)<0,"customer sidebar must not expose the standalone "+route+" entry");assert.ok(sidebarRoutes.indexOf(route)<0,"removed customer entry must not stay in any sidebar group: "+route);});
["customer-add","customer-edit","customer-subscriptions"].forEach(function(route){var screen=registry.screens.find(function(item){return item.route===route;});assert.ok(screen,"customer route must remain registered: "+route);assert.ok(screen.navigationHidden,"customer action route must stay out of navigation and search: "+route);});
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="customer-add";}).renderer,"entity-form","customer creation must use the real customer form");
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="customer-edit";}).renderer,"entity-form","customer editing must use the real customer form");
assert.ok(appJs.indexOf('"customer-add"')>=0&&appJs.indexOf('"customer-edit"')>=0,"customer creation and editing routes must render their real forms");
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="customers-list";}).renderer,"entity-list","customer management must use the shared entity list");
assert.ok(renderersJs.indexOf("data-list-add")>=0&&renderersJs.indexOf('data-row-action="edit"')>=0,"customer management must expose Arabic add and edit actions");
assert.ok(renderersJs.indexOf('info.singular.replace(/^ال/, "")')>=0,"the customer management add action must name the Arabic entity");
assert.ok(renderersJs.indexOf('method: isEdit ? "PUT" : "POST"')>=0&&renderersJs.indexOf('"/api/customers"')>=0,"customer creation and editing must persist through the customer API");
assert.deepStrictEqual(Array.from(inventoryModule.items),["products-list","warehouses-list"],"inventory sidebar must contain only Product Catalog and Warehouse Directory");
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="warehouses-list";}).renderer,"warehouse-directory","Warehouse Directory must resolve to its real renderer");
assert.ok(inventoryJs.indexOf('/api/inventory/warehouses')>=0&&inventoryJs.indexOf('/api/inventory/balances')>=0&&inventoryJs.indexOf('data-select-warehouse')>=0,"Warehouse Directory must query persisted warehouses and balances with searchable selection");
assert.ok(inventoryJs.indexOf('receiptModal')>=0&&inventoryJs.indexOf('data-operation")==="manual_receipt"')>=0,"the add-stock action must open the simplified receipt modal");
assert.ok(inventoryJs.indexOf('data-save-receipt')>=0&&inventoryJs.indexOf(">حفظ<")>=0&&inventoryJs.indexOf('جارٍ الحفظ')>=0,"the simplified receipt must save directly with a disabled-while-saving button");
var receiptSource=inventoryJs.slice(inventoryJs.indexOf('    function receiptModal('));
assert.ok(receiptSource.indexOf('سعر الشراء الجديد')>=0&&receiptSource.indexOf('سعر البيع الجديد')>=0,"receipt must request explicit acquisition and selling prices");
assert.ok(receiptSource.indexOf('unitCost:plus?cost:null')>=0&&receiptSource.indexOf('salePrice:receipt?sale:null')>=0,"receipt must post selected-unit prices without a reference-cost fallback");
assert.ok(inventoryJs.indexOf('name="reason"')<0&&inventoryJs.indexOf('countReason')<0&&inventoryJs.indexOf('setTimeout')<0,"warehouse forms must not contain reason fields or timer-based hiding");
assert.ok(receiptSource.indexOf('data-review')<0&&receiptSource.indexOf('variantField.hidden=state.variants.length<=1')>=0,"receipt must save directly and show variants only when needed");
assert.ok(inventoryJs.indexOf("<th>المخزن</th>")>=0&&inventoryJs.indexOf("<th>ملاحظات</th>")>=0&&inventoryJs.indexOf("x.warehouseName")>=0,"movement history must show the warehouse and optional notes in Arabic");
var hiddenInventoryActions=["product-add","product-edit","product-quick-add","warehouse-add","stock-adjustment","warehouse-transfer","stock-movements","low-stock","manufacturing"];
hiddenInventoryActions.forEach(function(route){var screen=registry.screens.find(function(item){return item.route===route;});assert.ok(screen&&screen.navigationHidden,"action-only inventory route must remain registered but hidden from top-level discovery: "+route);assert.strictEqual(typeof screen.renderer,"string");});
assert.ok(settingsStateJs.indexOf("loadPromise")>=0&&settingsStateJs.indexOf("/api/settings/general")>=0,"settings reads must be deduplicated");
assert.ok(!registry.modules.some(function(module){return module.id==="permissions"||module.id==="printing";}),"standalone permissions and configuration-only printing groups must be removed from the sidebar");
assert.ok(!sidebarRoutes.some(function(route){return route==="users-list"||route==="user-permissions"||route==="sales-permissions"||route==="settings-permissions";}),"user administration routes must not appear directly in sidebar groups");
assert.ok(sidebarRoutes.indexOf("shift-approval")>=0&&registry.modules.find(function(module){return module.id==="employees";}).items.indexOf("shift-approval")>=0,"shift approval must remain with employee operations");
var usersScreen=registry.screens.find(function(screen){return screen.route==="users-list";});
assert.strictEqual(usersScreen.renderer,"user-management","User Management must resolve to its real renderer");
assert.strictEqual(usersScreen.settingsGroup,"users-security","User Management must be located in the Settings security group");
assert.ok(usersScreen.administratorOnly,"User Management must remain administrator-only");
var legacyPermissionRoutes=["user-permissions","sales-permissions","settings-permissions"];
legacyPermissionRoutes.forEach(function(route){var screen=registry.screens.find(function(item){return item.route===route;});assert.strictEqual(screen.redirectTo,"users-list","legacy permission routes must redirect to User Management");assert.ok(screen.navigationHidden,"legacy permission routes must stay out of navigation and search");});
assert.ok(registry.screens.filter(function(screen){return screen.administratorOnly;}).every(function(screen){return screen.route==="settings-program"||screen.settingsGroup||screen.navigationHidden;}),"every administrative screen must be reachable from Settings or be an explicit hidden compatibility route");
var settingsEntries=registry.screens.filter(function(screen){return screen.settingsGroup&&!screen.navigationHidden;});
settingsEntries.forEach(function(screen){assert.ok(routes.indexOf(screen.route)>=0&&allowedRenderers.has(screen.renderer),"Settings entry must resolve to a registered renderer: "+screen.route);});
assert.ok(appJs.indexOf("typeof renderers[screen.renderer]")>=0,"startup must fail visibly when registry renderer code is missing");
assert.ok(mainFormCs.indexOf("Network.clearBrowserCache")>=0&&serverHostCs.indexOf("no-store, no-cache, must-revalidate")>=0,"desktop startup and static serving must prevent shell/registry cache version skew");
assert.ok(appJs.indexOf("resolveRoute")>=0&&appJs.indexOf("screen.redirectTo")>=0,"legacy route compatibility must use centralized route resolution");
function verifyRuntimeRendererResolution(){var context={window:{AlKhayalUtils:{},AlKhayalUI:{},AlKhayalServices:{},AlKhayalCalculations:{},AlKhayalApi:{},AlKhayalFeatures:{},AlKhayalSettings:{},AlKhayalTheme:{}}};context.window.window=context.window;[renderersJs,catalogJs,inventoryJs,activityJs,featureJs,settingsHubJs,generalSettingsJs,userManagementJs,dashboardJs,posJs,purchasingJs].forEach(function(source,index){vm.runInNewContext(source,context,{filename:"renderer-"+index+".js"});});registry.screens.forEach(function(screen){assert.strictEqual(typeof context.window.AlKhayalRenderers[screen.renderer],"function","route must resolve to executable renderer: "+screen.route+" -> "+screen.renderer);});}
verifyRuntimeRendererResolution();
function verifyRepeatedAdministrationRendering(){var requests=0;var utils={normalize:function(value){return String(value||"").toLowerCase();},escape:function(value){return String(value==null?"":value);}};var ui={pageHeader:function(){return "";},field:function(){return "";}};var context={window:{AlKhayalUtils:utils,AlKhayalUI:ui,AlKhayalFeatures:{routeEnabled:function(){return true;}},AlKhayalApi:{request:function(){requests+=1;return new Promise(function(){});}},AlKhayalRenderers:{},confirm:function(){return true;}}};context.window.window=context.window;vm.runInNewContext(settingsHubJs,context,{filename:"settings-hub-repeat.js"});vm.runInNewContext(userManagementJs,context,{filename:"user-management-repeat.js"});function container(){var input={focus:function(){},setSelectionRange:function(){}};return{innerHTML:"",querySelector:function(selector){return selector==="[data-settings-search]"?input:null;},querySelectorAll:function(){return[];}};}var renderContext={registry:registry,user:{role:"administrator",id:"admin"},canNavigate:function(){return true;},navigate:function(){},isActive:function(){return true;}};var settings=registry.screens.find(function(screen){return screen.route==="settings-program";}),users=registry.screens.find(function(screen){return screen.route==="users-list";});for(var index=0;index<5;index+=1){context.window.AlKhayalRenderers["settings-hub"](settings,container(),renderContext);context.window.AlKhayalRenderers["user-management"](users,container(),renderContext);}assert.strictEqual(requests,5,"five User Management reopen cycles must issue one bounded list request each without recursive loads");}
verifyRepeatedAdministrationRendering();
var responsiveViewports = [[1920, 1080], [1600, 900], [1366, 768], [1280, 720], [1024, 768], [900, 600]];
assert.strictEqual(responsiveViewports.length, 6, "responsive viewport matrix is explicit");

async function verifyFeatureStateLifecycle() {
  var resolveRequest, requestCount = 0, notifications = 0;
  var context = {
    CustomEvent: function (name, options) { this.type = name; this.detail = options.detail; },
    window: {
      AlKhayalApi: { request: function () { requestCount += 1; return new Promise(function (resolve) { resolveRequest = resolve; }); } },
      dispatchEvent: function () { notifications += 1; }
    }
  };
  vm.runInNewContext(featureStateJs, context, { filename: "feature-state.js" });
  var initial = { modules: [{ key: "installments", isEnabled: false }], capabilities: [], generatedUtc: "2026-10-21T10:00:00Z" };
  var first = context.window.AlKhayalFeatures.load({ notify: false });
  var second = context.window.AlKhayalFeatures.load({ notify: false });
  var third = context.window.AlKhayalFeatures.load({ notify: false });
  assert.strictEqual(requestCount, 1, "concurrent feature loads must share one API request");
  assert.strictEqual(first, second); assert.strictEqual(second, third);
  resolveRequest(initial); await Promise.all([first, second, third]);
  assert.strictEqual(notifications, 0, "screen-owned refreshes must not notify the shell");
  context.window.AlKhayalFeatures.setSnapshot({ modules: initial.modules, capabilities: [], generatedUtc: "2026-10-21T10:01:00Z" });
  assert.strictEqual(notifications, 0, "timestamp-only snapshots must not trigger navigation reconciliation");
  context.window.AlKhayalFeatures.setSnapshot({ modules: [{ key: "installments", isEnabled: true }], capabilities: [] });
  assert.strictEqual(notifications, 1, "an effective feature change must notify the shell exactly once");
}

async function verifyAccessStateLifecycle() {
  var resolveRequest, requestCount=0, notifications=0;
  var context={CustomEvent:function(name,options){this.type=name;this.detail=options.detail;},window:{AlKhayalApi:{request:function(){requestCount+=1;return new Promise(function(resolve){resolveRequest=resolve;});}},dispatchEvent:function(){notifications+=1;}}};
  vm.runInNewContext(accessStateJs,context,{filename:"access-state.js"});
  var first=context.window.AlKhayalAccess.load({notify:false}),second=context.window.AlKhayalAccess.load({notify:false});
  assert.strictEqual(requestCount,1,"concurrent access loads must share one API request");assert.strictEqual(first,second);
  resolveRequest({role:"cashier",screenIds:["dashboard"]});await Promise.all([first,second]);assert.strictEqual(notifications,0,"screen-owned access loads must not notify the shell");
  context.window.AlKhayalAccess.setSnapshot({role:"cashier",screenIds:["dashboard"]});assert.strictEqual(notifications,0,"unchanged access must not trigger reconciliation");
  context.window.AlKhayalAccess.setSnapshot({role:"cashier",screenIds:["dashboard","pos"]});assert.strictEqual(notifications,1,"an access change must notify exactly once");
}

async function verifySettingsStateLifecycle() {
  var resolveRequest,requestCount=0,notifications=0,storage={};
  var context={Intl:Intl,Date:Date,JSON:JSON,CustomEvent:function(name,options){this.type=name;this.detail=options.detail;},window:{AlKhayalUtils:{},AlKhayalApi:{request:function(){requestCount+=1;return new Promise(function(resolve){resolveRequest=resolve;});}},dispatchEvent:function(){notifications+=1;}}};
  vm.runInNewContext(settingsStateJs,context,{filename:"settings-state.js"});
  var first=context.window.AlKhayalSettings.load({notify:false}),second=context.window.AlKhayalSettings.load({notify:false});assert.strictEqual(requestCount,1,"concurrent settings loads must share one API request");assert.strictEqual(first,second);
  resolveRequest({dateFormat:"dd/MM/yyyy",timeFormat:"HH:mm",useGroupSeparators:true,currencySymbol:"ج.م",decimalPrecision:2});await Promise.all([first,second]);assert.strictEqual(notifications,0,"initial settings formatting must not recursively notify the shell");
  assert.ok(context.window.AlKhayalUtils.money(10).indexOf("ج.م")>=0,"shared money formatter must consume the persisted symbol");
  assert.ok(context.window.AlKhayalUtils.dateTime(new Date(2026,9,21,13,5)).indexOf("21/10/2026")>=0,"shared date-time formatter must consume persisted formats");
  context.window.AlKhayalSettings.setSnapshot({dateFormat:"yyyy-MM-dd",timeFormat:"HH:mm",useGroupSeparators:true,currencySymbol:"$",decimalPrecision:2});assert.strictEqual(notifications,1,"a real settings change may notify exactly once");
}

var totals = calculations.invoice([{ quantity: 2, price: 19.95 }, { quantity: 3, price: 10 }], 4.90, 14);
assert.deepStrictEqual(totals, { subtotal: 69.9, discount: 4.9, tax: 9.1, total: 74.1 });
var schedule = calculations.installments(1000, 100, 3, "2026-11-01");
assert.strictEqual(schedule.length, 3); assert.strictEqual(schedule.reduce(function (sum, item) { return sum + item.amount; }, 0), 900); assert.strictEqual(schedule[2].dueDate, "2027-01-01");
var sample = [{ name: "أحمد", amount: 2 }, { name: "محمود", amount: 1 }];
assert.strictEqual(calculations.filter(sample, "أح", ["name"]).length, 1); assert.strictEqual(calculations.sort(sample, "amount", "asc")[0].amount, 1);
function verifyCatalogSavedSummaryAndEditHydration(){
  var source=catalogJs.replace('  R["catalog-products"]=products;', '  global.__catalogTest={catalogSummaryBadges:catalogSummaryBadges,capabilityDetails:capabilityDetails};\n  R["catalog-products"]=products;');
  assert.notStrictEqual(source,catalogJs,"catalog test hook must attach to the registered module");
  var utils={escape:function(value){return String(value==null?"":value).replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/"/g,"&quot;");},number:function(value){return String(value);},money:function(value){return String(value);}};
  var context={window:{AlKhayalUtils:utils,AlKhayalUI:{badge:function(value){return "<span>"+value+"</span>";}},AlKhayalApi:{},AlKhayalRenderers:{}}};
  vm.runInNewContext(source,context,{filename:"catalog-summary-hydration.js"});
  var helpers=context.window.__catalogTest;
  assert.strictEqual(helpers.catalogSummaryBadges({sizeValues:[],colorValues:[],variantCount:1}),"","simple products must have no optional badges");
  assert.ok(helpers.catalogSummaryBadges({sizeValues:[],colorValues:[],variantCount:1,hasBatchTracking:true}).indexOf("تشغيلات")>=0,"batch-only products need a compact persisted tracking badge");
  assert.ok(helpers.catalogSummaryBadges({sizeValues:[],colorValues:[],variantCount:1,hasExpiryTracking:true,hasBatchTracking:true}).indexOf("تشغيلات")<0,"expiry and its required batch must not create redundant badges");
  var badges=helpers.catalogSummaryBadges({sizeValues:["M","L","XL"],colorValues:["أسود","أبيض"],variantCount:6,hasExpiryTracking:true});
  assert.ok(badges.indexOf("مقاسات: M، L +1")>=0&&badges.indexOf("ألوان: أسود، أبيض")>=0&&badges.indexOf("تركيبات: 6")>=0&&badges.indexOf("صلاحية")>=0,"badges must contain actual values and configuration");
  assert.ok(badges.indexOf('title="مقاسات: M، L، XL"')>=0,"the full value list must remain accessible");
  var record={id:7,variantMode:true,attributes:[{attributeId:1,kind:"size",values:[{valueId:11,value:"M"},{valueId:12,value:"L"}]},{attributeId:2,kind:"color",values:[{valueId:21,value:"أسود"},{valueId:22,value:"أبيض"}]}],capabilities:[{key:"sizes",isAssigned:true},{key:"colors",isAssigned:true}],variants:[{id:101,isDefault:true,isActive:true,attributeValueIds:[11,21],sku:"SKU-1",barcodes:[{code:"BC-1",isRetired:false}]},{id:102,isDefault:false,isActive:true,attributeValueIds:[11,22],barcodes:[]},{id:103,isDefault:false,isActive:true,attributeValueIds:[12,21],barcodes:[]},{id:104,isDefault:false,isActive:true,attributeValueIds:[12,22],barcodes:[]}]};
  var snapshot={capabilities:[{key:"sizes",isEnabled:true},{key:"colors",isEnabled:true}]};
  var html=helpers.capabilityDetails(record,[],[],true,snapshot);
  assert.ok(html.indexOf('data-generate-barcode')>=0&&html.indexOf('إنشاء باركود')>=0&&html.indexOf('catalogSku')<0,"the simplified product form must offer barcode entry/generation without a visible SKU field");
  assert.ok(html.indexOf('value="M"')>=0&&html.indexOf('value="L"')>=0&&html.indexOf('value="أسود"')>=0&&html.indexOf('value="أبيض"')>=0,"Edit must restore saved free-text values even when the global attribute list is unavailable");
  assert.ok(html.indexOf('data-capability-toggle="sizes" checked')>=0&&html.indexOf('data-capability-toggle="colors" checked')>=0,"saved switches must restore ON state");
  assert.ok(html.indexOf('data-capability-toggle="batch-tracking"')<0&&html.indexOf('data-capability-toggle="serial-numbers"')<0&&html.indexOf('data-capability-toggle="imei-tracking"')<0,"legacy tracking controls must never be presented in product creation/editing");
  assert.ok(html.indexOf('data-variant-id="101"')>=0&&html.indexOf('data-variant-id="104"')>=0&&html.indexOf('SKU-1')>=0&&html.indexOf('BC-1')>=0,"variant summary must show saved combinations and identifiers");
  assert.ok(html.indexOf('<td>11, 21</td>')<0,"variant summary must show value labels instead of raw IDs");
  var disabled=helpers.capabilityDetails(record,[],[],true,{capabilities:[{key:"sizes",isEnabled:false},{key:"colors",isEnabled:false}]});
  assert.ok(disabled.indexOf('value="M"')>=0&&disabled.indexOf('value="M" readonly')<0,"simplified product properties must remain available even when legacy global switches are disabled");
}
verifyCatalogSavedSummaryAndEditHydration();
(function verifySupplierWiring(){
  assert.ok(renderersJs.indexOf('"/api/suppliers"')>=0&&renderersJs.indexOf('supplierStatement')>=0&&renderersJs.indexOf('supplierPayment')>=0,"supplier screens must use the persisted suppliers API");
  assert.ok(renderersJs.indexOf('data-pay-invoice')>=0&&renderersJs.indexOf('دفع آجل')>=0&&renderersJs.indexOf('/payments')>=0,"supplier invoices must expose a credit-payment button wired to the payments endpoint");
  assert.ok(renderersJs.indexOf('/invoices')>=0,"supplier account page must load invoices from the API");
  ["suppliers-list","supplier-credit","supplier-payment","supplier-balance"].forEach(function(route){var screen=registry.screens.find(function(item){return item.route===route;});assert.ok(screen&&screen.description.indexOf("تجريبي")<0,"supplier screen must not be described as a demo: "+route);});
}());
function verifyCustomerManagementCreateAndEdit(){
  function fakeElement(){var element={innerHTML:"",textContent:"",value:"",hidden:false,selectors:{},classList:{add:function(){},remove:function(){},toggle:function(){},contains:function(){return false;}},querySelector:function(selector){if(!element.selectors[selector])element.selectors[selector]=fakeElement();return element.selectors[selector];},querySelectorAll:function(){return[];},addEventListener:function(){},removeEventListener:function(){},focus:function(){},setAttribute:function(){},removeAttribute:function(){},appendChild:function(child){return child;},remove:function(){},click:function(){}};return element;}
  var modalLayer=fakeElement(),toastStack=fakeElement(),apiCalls=[];
  modalLayer.selectors["#recordForm"]=fakeElement();
  modalLayer.selectors["#recordForm"].elements=new Proxy({}, {get:function(target,key){return typeof key==="string"?{value:"CUS-900"}:undefined;}});
  var apiStub={request:function(target,options){apiCalls.push({target:target,options:options||{}});return Promise.resolve({success:true,data:[]});}};
  var documentStub={activeElement:null,getElementById:function(id){if(id==="modalLayer")return modalLayer;if(id==="toastStack")return toastStack;return null;},createElement:function(){return fakeElement();},body:{appendChild:function(){}}};
  var context={window:{},console:console,document:documentStub,localStorage:{getItem:function(){return null;},setItem:function(){}},setTimeout:function(){return 0;},clearTimeout:function(){},AlKhayalApi:apiStub};
  context.window.window=context.window;
  function mirrorGlobals(){["AlKhayalUtils","AlKhayalCalculations","AlKhayalMockSeed","AlKhayalServices","AlKhayalUI","AlKhayalApi"].forEach(function(key){if(context[key]!==undefined)context.window[key]=context[key];});}
  ["frontend/js/core/utils.js","frontend/js/core/calculations.js","frontend/js/mocks/data.js","frontend/js/services/mock-services.js","frontend/js/components/ui.js"].forEach(function(relative){vm.runInNewContext(fs.readFileSync(path.join(root,relative),"utf8"),context,{filename:relative});mirrorGlobals();});
  vm.runInNewContext(renderersJs,context,{filename:"renderers.js"});
  var screen=registry.screens.find(function(item){return item.route==="customers-list";});
  var container={innerHTML:"",querySelector:function(selector){return selector==="#listSearch"?{addEventListener:function(){}}:null;},querySelectorAll:function(){return[];}};
  var renderContext={state:{},navigate:function(){},canNavigate:function(){return true;},registry:registry,user:{role:"administrator"},isActive:function(){return true;}};
  function render(){context.window.AlKhayalRenderers["entity-list"](screen,container,renderContext,true);}
  render();
  assert.ok(container.innerHTML.indexOf('data-list-add>+ إضافة عميل<')>=0,"customer management must expose an Arabic add-customer action");
  assert.ok(container.innerHTML.indexOf('data-row-action="edit"')>=0&&container.innerHTML.indexOf(">تعديل<")>=0,"customer management must expose an Arabic edit action for each customer");
  var row={getAttribute:function(name){return name==="data-row-id"?"1":null;}};
  return container.onclick({target:{closest:function(selector){if(selector==="tr[data-row-id]")return row;if(selector==="[data-row-action]")return {getAttribute:function(){return "edit";},closest:function(){return row;}};return null;}}}).then(function(){
    assert.ok(modalLayer.innerHTML.indexOf("تعديل بيانات العميل")>=0,"the customer edit action must open the Arabic customer edit form");
    assert.ok(modalLayer.innerHTML.indexOf('value="CUS-001"')>=0&&modalLayer.innerHTML.indexOf('value="أحمد محمد علي"')>=0,"the customer edit form must open with the saved customer values");
    return modalLayer.selectors["[data-save-record]"].onclick();
  }).then(function(){
    var update=apiCalls.filter(function(call){return call.target==="/api/customers/1"&&call.options.method==="PUT";});
    assert.strictEqual(update.length,1,"the customer edit form must save through the persisted customer API");
    context.window.AlKhayalServices.store.replace("customers",context.window.AlKhayalMockSeed.customers);
    render();
    return container.onclick({target:{closest:function(selector){return selector==="[data-list-add]"?{}:null;}}});
  }).then(function(){
    assert.ok(modalLayer.innerHTML.indexOf("إضافة عميل")>=0,"the customer management screen must open the Arabic add-customer form");
    assert.ok(modalLayer.innerHTML.indexOf('id="code"')>=0&&modalLayer.innerHTML.indexOf('id="name"')>=0&&modalLayer.innerHTML.indexOf('id="phone"')>=0,"the add-customer form must keep the Arabic customer fields");
    return modalLayer.selectors["[data-save-record]"].onclick();
  }).then(function(){
    var create=apiCalls.filter(function(call){return call.target==="/api/customers"&&call.options.method==="POST";});
    assert.strictEqual(create.length,1,"the add-customer form must save through the persisted customer API");
    var formElement=fakeElement();
    formElement.elements=new Proxy({}, {get:function(target,key){return typeof key==="string"?{value:"CUS-777"}:undefined;}});
    formElement.reset=function(){};
    var addScreen=registry.screens.find(function(item){return item.route==="customer-add";});
    var formContainer={innerHTML:"",querySelector:function(selector){return selector==="#entityForm"?formElement:null;},querySelectorAll:function(){return[];}};
    context.window.AlKhayalRenderers["entity-form"](addScreen,formContainer,renderContext,true);
    assert.ok(formContainer.innerHTML.indexOf('id="entityForm"')>=0&&formContainer.innerHTML.indexOf('id="code"')>=0&&formContainer.innerHTML.indexOf('id="name"')>=0,"the contextual customer-add route must still open the real customer form");
    return formElement.onsubmit({preventDefault:function(){}});
  }).then(function(){
    var routeCreate=apiCalls.filter(function(call){return call.target==="/api/customers"&&call.options.method==="POST";});
    assert.strictEqual(routeCreate.length,2,"the contextual customer-add route must save through the persisted customer API");
  });
}
Promise.all([require("./inventory-tests.js")(),verifyFeatureStateLifecycle(),verifyAccessStateLifecycle(),verifySettingsStateLifecycle(),verifyCustomerManagementCreateAndEdit()]).then(function () { console.log("Frontend registry, settings/feature/access lifecycle, user management, customer management, and calculation tests passed (" + routes.length + " screens)."); }).catch(function (error) { console.error(error); process.exitCode = 1; });
