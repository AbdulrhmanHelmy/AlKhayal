using System;
using System.Collections.Generic;
using System.Linq;

namespace AlKhayal.Application.Features
{
    public static class FeatureRegistry
    {
        private static readonly IReadOnlyList<FeatureDefinition> Items = new[]
        {
            Module("authentication", "المصادقة", "تسجيل الدخول والجلسات المحلية.", true, true),
            Module("basic-settings", "الإعدادات الأساسية", "إعدادات المنشأة والبرنامج.", true, true),
            Module("user-administration", "إدارة المستخدمين", "إدارة مستخدمي النظام.", true, true),
            Module("product-catalog", "دليل المنتجات", "تعريف المنتجات والبحث فيها.", true, true),
            Module("basic-inventory", "المخزون الأساسي", "الأرصدة والحركات الأساسية.", true, true),
            Module("sales", "المبيعات", "نقطة البيع وإدارة المبيعات.", true, true),
            Module("purchases", "المشتريات", "إدارة المشتريات والموردين.", true, true),
            Module("customers", "العملاء", "بيانات العملاء وحساباتهم.", true, true),
            Module("suppliers", "الموردون", "بيانات الموردين وحساباتهم.", true, true),
            Module("core-finance", "البنية المالية الأساسية", "الخزنة والمصروفات والحركات الأساسية.", true, true),
            Module("maintenance-management", "إدارة الصيانة", "استلام الأجهزة ومتابعة أعمال الصيانة.", false, false),
            Module("installments", "البيع بالتقسيط", "العقود والجداول ومتابعة السداد.", false, false),
            Module("manufacturing", "التصنيع", "تركيبات التصنيع والمدخلات والمخرجات.", false, false),
            Module("multiple-warehouses", "المخازن المتعددة", "تعريف أكثر من مخزن والتحويل بينها.", false, false),
            Module("multiple-branches", "الفروع المتعددة", "تعريف الفروع وحركاتها.", false, false),
            Module("employee-attendance", "حضور الموظفين", "تسجيل ومتابعة الحضور والانصراف.", false, false),
            Module("advanced-shifts", "إدارة الورديات المتقدمة", "تعريف الورديات وتقاريرها.", false, false),
            Module("cash-services", "خدمات الكاش", "واجهات خدمات الدفع والشحن.", false, false),
            Module("advanced-reports", "التقارير المتقدمة", "تحليلات وتقارير تشغيلية موسعة.", false, false),
            Module("barcode-label-printing", "طباعة ملصقات الباركود", "تصميم ومعاينة وطباعة الملصقات.", false, false),
            Capability("sizes", "المقاسات", "تعريف مقاسات قابلة للتطبيق على المنتج."),
            Capability("colors", "الألوان", "تعريف ألوان قابلة للتطبيق على المنتج."),
            Capability("product-variants", "متغيرات المنتج", "تركيبات المنتج من السمات.", "sizes", "colors"),
            Capability("serial-numbers", "الأرقام التسلسلية", "تتبع رقم تسلسلي فريد عند الحاجة."),
            Capability("imei-tracking", "تتبع IMEI", "تتبع معرفات أجهزة الهاتف.", "serial-numbers"),
            Capability("warranty-tracking", "تتبع الضمان", "حفظ معلومات الضمان للمنتجات القابلة لذلك."),
            Capability("expiry-dates", "تواريخ الصلاحية", "تتبع صلاحية المنتجات القابلة للتلف."),
            Capability("batch-tracking", "تتبع التشغيلات", "تتبع الدفعات وأرقام التشغيلات."),
            Capability("multiple-units", "الوحدات المتعددة", "بيع وشراء المنتج بوحدات متعددة."),
            Capability("weight-based-products", "المنتجات الموزونة", "تعريف منتجات تباع بالوزن."),
            Capability("product-barcode-settings", "إعدادات باركود المنتج", "إعداد الباركود على مستوى المنتج."),
            Capability("barcode-labels", "ملصقات الباركود", "تمكين المنتج من استخدام بيانات الملصق."),
            Capability("barcode-scanning", "مسح الباركود", "استخدام الباركود في البحث والإدخال.", true),
            Capability("wholesale-pricing", "أسعار الجملة", "تعريف أسعار جملة للمنتج."),
            Capability("inventory", "المخزون", "ربط المنتج بالمخزون الأساسي.", true),
            Capability("sales", "المبيعات", "إتاحة المنتج للبيع.", true)
        };

        public static IReadOnlyList<FeatureDefinition> All { get { return Items; } }
        public static FeatureDefinition Find(string key) { return Items.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)); }
        private static FeatureDefinition Module(string key, string name, string description, bool core, bool enabled, params string[] dependencies) { return new FeatureDefinition { Key = key, Kind = FeatureKinds.Module, ArabicName = name, Description = description, IsCore = core, DefaultEnabled = enabled, Dependencies = dependencies }; }
        private static FeatureDefinition Capability(string key, string name, string description, params string[] dependencies) { return Capability(key, name, description, false, dependencies); }
        private static FeatureDefinition Capability(string key, string name, string description, bool core, params string[] dependencies) { return new FeatureDefinition { Key = key, Kind = FeatureKinds.Capability, ArabicName = name, Description = description, IsCore = core, DefaultEnabled = core, Dependencies = dependencies }; }
    }
}
