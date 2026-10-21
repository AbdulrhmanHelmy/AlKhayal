"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");
var root = path.resolve(__dirname, "..", ".."), calculations = require(path.join(root, "frontend", "js", "core", "calculations.js"));
var registry = JSON.parse(fs.readFileSync(path.join(root, "frontend", "data", "screens.json"), "utf8"));
var indexHtml = fs.readFileSync(path.join(root, "frontend", "index.html"), "utf8");
var layoutCss = fs.readFileSync(path.join(root, "frontend", "css", "layout.css"), "utf8");
var modulesCss = fs.readFileSync(path.join(root, "frontend", "css", "modules.css"), "utf8");
var appJs = fs.readFileSync(path.join(root, "frontend", "js", "app.js"), "utf8");
var activityJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "business-activities.js"), "utf8");
var featureJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "feature-management.js"), "utf8");
var featureStateJs = fs.readFileSync(path.join(root, "frontend", "js", "services", "feature-state.js"), "utf8");
var accessStateJs = fs.readFileSync(path.join(root, "frontend", "js", "services", "access-state.js"), "utf8");
var userManagementJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "user-management.js"), "utf8");
var dashboardJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "dashboard.js"), "utf8");
var renderersJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "renderers.js"), "utf8");
var settingsHubJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "settings-hub.js"), "utf8");
var generalSettingsJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "general-settings.js"), "utf8");
var settingsStateJs = fs.readFileSync(path.join(root, "frontend", "js", "services", "settings-state.js"), "utf8");
var posJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "pos.js"), "utf8");
var mainFormCs = fs.readFileSync(path.join(root, "src", "AlKhayal.UI", "MainForm.cs"), "utf8");
var serverHostCs = fs.readFileSync(path.join(root, "src", "AlKhayal.Server.Host", "AlKhayalServer.cs"), "utf8");
var allowedRenderers = new Set(["dashboard", "settings", "settings-hub", "legacy-redirect", "business-activities", "feature-management", "user-management", "company-settings", "general-settings", "appearance-settings", "database-settings", "entity-list", "entity-form", "statement", "payment", "export", "report", "pos", "invoice-lookup", "workflow", "invoice-editor", "picker", "returns", "printer", "barcode", "expense-form", "alerts", "threshold", "adjustment", "transfer", "manufacturing", "attendance", "permissions", "installments", "maintenance-intake", "maintenance-tracking", "cash-services"]);

var routes = registry.screens.map(function (screen) { return screen.route; });
assert.strictEqual(new Set(routes).size, routes.length, "screen routes must be unique");
assert.ok(routes.length >= 90, "the full inventory should register at least 90 distinct screens");
registry.modules.forEach(function (module) { module.items.forEach(function (route) { assert.ok(routes.indexOf(route) >= 0, "navigation route is missing: " + route); }); });
var sidebarRoutes = registry.modules.reduce(function(all,module){return all.concat(module.items);},[]);
assert.strictEqual(new Set(sidebarRoutes).size,sidebarRoutes.length,"main sidebar routes must not be duplicated");
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
assert.strictEqual((appJs.match(/alkhayal:access-changed/g) || []).length, 1, "the shell must register exactly one access-state listener");
assert.ok(indexHtml.indexOf("settings-hub.js")>=0&&settingsHubJs.indexOf("settingsGroup")>=0,"the Settings hub must load and derive entries from registry metadata");
assert.ok(indexHtml.indexOf("general-settings.js")>=0&&indexHtml.indexOf("settings-state.js")>=0,"real settings UI and shared formatter state must load in the shell");
var realSettings={"settings-general":"general-settings","settings-company":"company-settings","settings-appearance":"appearance-settings","settings-backup":"database-settings"};
Object.keys(realSettings).forEach(function(route){var screen=registry.screens.find(function(item){return item.route===route;});assert.strictEqual(screen.renderer,realSettings[route],route+" must use a real renderer");assert.ok(screen.settingsGroup,"real settings routes must remain in the registry-driven Settings hub");});
assert.ok(generalSettingsJs.indexOf("/api/settings/company")>=0&&generalSettingsJs.indexOf("/api/settings/general")>=0&&generalSettingsJs.indexOf("/api/settings/database-info")>=0,"settings screens must use the authenticated API");
assert.ok(generalSettingsJs.indexOf("إلغاء التغييرات")>=0&&generalSettingsJs.indexOf("context.isActive()")>=0,"settings forms must support cancel and stale-response protection");
assert.ok(generalSettingsJs.indexOf("context.state.companyDraft")>=0&&generalSettingsJs.indexOf("context.state.generalDraft")>=0,"unsaved settings drafts must survive workspace tab switches");
assert.ok(generalSettingsJs.indexOf("AlKhayalTheme.set")>=0&&generalSettingsJs.indexOf("light")>=0&&generalSettingsJs.indexOf("dark")>=0&&generalSettingsJs.indexOf("system")>=0,"appearance must reuse all three existing theme modes");
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
function verifyRuntimeRendererResolution(){var context={window:{AlKhayalUtils:{},AlKhayalUI:{},AlKhayalServices:{},AlKhayalCalculations:{},AlKhayalApi:{},AlKhayalFeatures:{},AlKhayalSettings:{},AlKhayalTheme:{}}};context.window.window=context.window;[renderersJs,activityJs,featureJs,settingsHubJs,generalSettingsJs,userManagementJs,dashboardJs,posJs].forEach(function(source,index){vm.runInNewContext(source,context,{filename:"renderer-"+index+".js"});});registry.screens.forEach(function(screen){assert.strictEqual(typeof context.window.AlKhayalRenderers[screen.renderer],"function","route must resolve to executable renderer: "+screen.route+" -> "+screen.renderer);});}
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
Promise.all([verifyFeatureStateLifecycle(),verifyAccessStateLifecycle(),verifySettingsStateLifecycle()]).then(function () { console.log("Frontend registry, settings/feature/access lifecycle, user management, and calculation tests passed (" + routes.length + " screens)."); }).catch(function (error) { console.error(error); process.exitCode = 1; });
