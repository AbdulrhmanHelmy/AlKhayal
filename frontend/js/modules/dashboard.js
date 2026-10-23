(function (global) {
  "use strict";
  var U = global.AlKhayalUtils, UI = global.AlKhayalUI;
  function dashboard(screen, container, context) {
    var destinations = [
      { route: "customers-list", title: "العملاء", text: "إدارة بيانات العملاء وأرصدتهم المحفوظة." },
      { route: "suppliers-list", title: "الموردون", text: "إدارة الموردين وفواتيرهم ومدفوعاتهم المحفوظة." },
      { route: "products-list", title: "المنتجات", text: "مراجعة دليل المنتجات والتصنيفات ووحدات القياس." },
      { route: "warehouses-list", title: "المخازن", text: "مراجعة المخازن والأرصدة والحركات الفعلية." }
    ].filter(function (item) { return !context.canNavigate || context.canNavigate(item.route); });
    container.innerHTML = UI.pageHeader(screen) +
      '<section class="card"><div class="card-head"><h2>ابدأ من هنا</h2></div><div class="card-body"><p>اختر قسمًا للعمل على البيانات المحفوظة. الشاشات غير المكتملة تعرض حالة واضحة ولا تنفذ عمليات تجريبية.</p><div class="stats-grid dashboard-destinations">' +
      destinations.map(function (item) { return '<button class="card stat-card" type="button" data-dashboard-route="' + U.escape(item.route) + '"><strong class="stat-value">' + U.escape(item.title) + '</strong><span class="stat-meta">' + U.escape(item.text) + '</span></button>'; }).join("") +
      '</div></div></section>';
    container.onclick = function (event) { var target = event.target.closest("[data-dashboard-route]"); if (target) context.navigate(target.getAttribute("data-dashboard-route")); };
  }
  global.AlKhayalRenderers.dashboard = dashboard;
}(window));
