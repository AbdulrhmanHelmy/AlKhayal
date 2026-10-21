using System;
using System.Collections.Generic;
using System.Linq;

namespace AlKhayal.Application.BusinessActivities
{
    public static class BusinessActivityRegistry
    {
        private static readonly IReadOnlyList<BusinessActivityDefinition> AllItems = new[]
        {
            Item("general-retail", "تجارة التجزئة العامة", "بيع منتجات متنوعة مباشرة للعملاء.", 10, "inventory", "sales", "barcode-scanning"),
            Item("grocery", "سوبر ماركت وبقالة", "مواد غذائية ومنتجات سريعة الدوران.", 20, "expiry-dates", "batch-tracking", "multiple-units", "weight-based-products", "barcode-scanning"),
            Item("clothing", "ملابس", "ملابس بمقاسات وألوان وتشكيلات متعددة.", 30, "sizes", "colors", "product-variants", "barcode-labels"),
            Item("footwear", "أحذية", "أحذية بمقاسات وألوان وموديلات متعددة.", 40, "sizes", "colors", "product-variants", "barcode-labels"),
            Item("mobile-phones", "هواتف محمولة", "أجهزة محمولة تحتاج تتبع الهوية والضمان.", 50, "serial-numbers", "imei-tracking", "warranty-tracking", "maintenance-management"),
            Item("electronics", "إلكترونيات", "أجهزة وملحقات إلكترونية مع الضمان والصيانة.", 60, "serial-numbers", "warranty-tracking", "maintenance-management"),
            Item("accessories", "إكسسوارات", "إكسسوارات هواتف وأجهزة ومنتجات مكملة.", 70, "product-variants", "barcode-labels", "barcode-scanning"),
            Item("cosmetics", "مستحضرات تجميل", "مستحضرات وعناية شخصية بتواريخ وتشغيلات.", 80, "expiry-dates", "batch-tracking", "product-variants"),
            Item("pharmacy-retail", "تجزئة مرتبطة بالصيدليات", "بيع تجزئة مرتبط بمنتجات صيدلية دون مسارات امتثال متخصصة.", 90, "expiry-dates", "batch-tracking", "barcode-scanning"),
            Item("hardware-electrical", "أدوات وكهرباء", "أدوات ومهمات كهربائية ووحدات بيع متنوعة.", 100, "multiple-units", "serial-numbers", "warranty-tracking"),
            Item("wholesale", "تجارة الجملة", "بيع كميات كبيرة ووحدات وأسعار متعددة.", 110, "multiple-units", "batch-tracking", "wholesale-pricing"),
            Item("maintenance-repair", "صيانة وإصلاح", "استلام أجهزة ومتابعة الإصلاح والتسليم.", 120, "maintenance-management", "serial-numbers", "warranty-tracking"),
            Item("other", "نشاط آخر أو مخصص", "نشاط غير مدرج؛ يمكن تخصيص جميع الإمكانات لاحقًا.", 130)
        };

        public static IReadOnlyList<BusinessActivityDefinition> All { get { return AllItems; } }
        public static BusinessActivityDefinition Find(string key) { return AllItems.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)); }

        private static BusinessActivityDefinition Item(string key, string name, string description, int priority, params string[] capabilities)
        {
            return new BusinessActivityDefinition { Key = key, ArabicName = name, Description = description, DefaultPriority = priority, IsActive = true, SuggestedCapabilities = capabilities };
        }
    }
}
