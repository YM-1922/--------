using Doctor.Web.Models.Entities;
using Doctor.Web.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(ApplicationDbContext context)
    {
        // Ensure Database is created
        await context.Database.EnsureCreatedAsync();

        // 1. Roles
        if (!await context.UserRoles.AnyAsync())
        {
            var adminRole = new UserRole { Name = "Admin", NameAr = "مدير النظام", Description = "صلاحيات كاملة على كافة أجزاء النظام" };
            var salesRole = new UserRole { Name = "Sales", NameAr = "مسؤول المبيعات", Description = "إدارة المبيعات ونقاط البيع والفواتير والعملاء" };
            var techRole = new UserRole { Name = "Maintenance", NameAr = "فني الصيانة", Description = "استلام وصيانة وفحص الأجهزة وإدارة قطع الغيار" };

            context.UserRoles.AddRange(adminRole, salesRole, techRole);
            await context.SaveChangesAsync();
        }

        var roles = await context.UserRoles.ToDictionaryAsync(r => r.Name, r => r.Id);

        // 2. Permissions
        if (!await context.Permissions.AnyAsync())
        {
            var permissions = new List<Permission>
            {
                // الإدارة
                new Permission { Code = "admin.access", NameAr = "الوصول للوحة الإدارة العامة", Category = "الإدارة" },
                new Permission { Code = "users.manage", NameAr = "إدارة المستخدمين وكلمات المرور", Category = "الإدارة" },
                new Permission { Code = "roles.manage", NameAr = "إدارة الأدوار والصلاحيات", Category = "الإدارة" },
                new Permission { Code = "settings.manage", NameAr = "إعدادات النظام والسنتر والواتساب", Category = "الإدارة" },
                new Permission { Code = "audit.view", NameAr = "مشاهدة سجل العمليات والتدقيق", Category = "الإدارة" },
                new Permission { Code = "reports.view", NameAr = "استعراض التقارير المالية والإدارية", Category = "الإدارة" },

                // المبيعات والـ POS
                new Permission { Code = "pos.access", NameAr = "الوصول لشاشة المبيعات (POS)", Category = "المبيعات" },
                new Permission { Code = "pos.create", NameAr = "إنشاء عمليات بيع وفواتير", Category = "المبيعات" },
                new Permission { Code = "sales.view", NameAr = "استعراض سجل المبيعات", Category = "المبيعات" },
                new Permission { Code = "sales.cancel", NameAr = "إلغاء أو إرجاع فواتير", Category = "المبيعات" },
                new Permission { Code = "invoices.view", NameAr = "مشاهدة الفواتير وطباعتها", Category = "المبيعات" },

                // المنتجات والمخزون
                new Permission { Code = "products.view", NameAr = "استعراض قائمة الأجهزة والإكسسوارات", Category = "المخزون" },
                new Permission { Code = "products.create", NameAr = "إضافة منتجات وأجهزة جديدة", Category = "المخزون" },
                new Permission { Code = "products.edit", NameAr = "تعديل بيانات وأسعار المنتجات", Category = "المخزون" },
                new Permission { Code = "products.delete", NameAr = "حذف منتجات", Category = "المخزون" },
                new Permission { Code = "barcode.print", NameAr = "توليد وطباعة الباركود", Category = "المخزون" },
                new Permission { Code = "inventory.view", NameAr = "متابعة حركة المخزن وتنبيهات النواقص", Category = "المخزون" },
                new Permission { Code = "inventory.adjust", NameAr = "تعديل الجرد اليدوي", Category = "المخزون" },

                // الصيانة
                new Permission { Code = "repairs.access", NameAr = "الوصول للوحة الصيانة", Category = "الصيانة" },
                new Permission { Code = "repairs.create", NameAr = "استلام جهاز جديد للصيانة", Category = "الصيانة" },
                new Permission { Code = "repairs.view", NameAr = "استعراض أجهزة وسجل الصيانة", Category = "الصيانة" },
                new Permission { Code = "repairs.status_change", NameAr = "تغيير وتحديث حالة الجهاز الفنية", Category = "الصيانة" },
                new Permission { Code = "repairs.deliver", NameAr = "إنهاء وتسليم الجهاز وتحصيل التكلفة", Category = "الصيانة" },
                new Permission { Code = "spareparts.view", NameAr = "مشاهدة مخزون قطع الغيار", Category = "الصيانة" },
                new Permission { Code = "spareparts.manage", NameAr = "إضافة وتعديل قطع الغيار", Category = "الصيانة" },

                // العملاء
                new Permission { Code = "customers.view", NameAr = "مشاهدة سجل العملاء", Category = "العملاء" },
                new Permission { Code = "customers.manage", NameAr = "إضافة وتعديل بيانات العملاء", Category = "العملاء" }
            };

            context.Permissions.AddRange(permissions);
            await context.SaveChangesAsync();

            // Link to roles
            var allPerms = await context.Permissions.ToListAsync();
            
            // Admin gets ALL
            foreach (var perm in allPerms)
            {
                context.RolePermissions.Add(new RolePermission { RoleId = roles["Admin"], PermissionId = perm.Id });
            }

            // Sales gets Sales, Products, POS, Customers, Barcode
            var salesCodes = new[] { "pos.access", "pos.create", "sales.view", "invoices.view", "products.view", "products.create", "products.edit", "barcode.print", "inventory.view", "customers.view", "customers.manage" };
            foreach (var perm in allPerms.Where(p => salesCodes.Contains(p.Code)))
            {
                context.RolePermissions.Add(new RolePermission { RoleId = roles["Sales"], PermissionId = perm.Id });
            }

            // Maintenance gets Repairs, SpareParts, Customers
            var techCodes = new[] { "repairs.access", "repairs.create", "repairs.view", "repairs.status_change", "repairs.deliver", "spareparts.view", "spareparts.manage", "customers.view", "customers.manage", "products.view" };
            foreach (var perm in allPerms.Where(p => techCodes.Contains(p.Code)))
            {
                context.RolePermissions.Add(new RolePermission { RoleId = roles["Maintenance"], PermissionId = perm.Id });
            }

            await context.SaveChangesAsync();
        }

        // 3. Users
        if (!await context.Users.AnyAsync())
        {
            var admin = new User
            {
                Username = "admin",
                FullName = "المهندس أحمد (المدير العام)",
                Email = "admin@doctor.com",
                PhoneNumber = "01000000001",
                PasswordHash = PasswordHasher.HashPassword("Admin@123"),
                RoleId = roles["Admin"],
                Status = UserStatus.Active,
                CreatedAt = DateTime.UtcNow
            };

            var salesUser = new User
            {
                Username = "sales",
                FullName = "محمود عادل (مسؤول المبيعات)",
                Email = "sales@doctor.com",
                PhoneNumber = "01000000002",
                PasswordHash = PasswordHasher.HashPassword("Sales@123"),
                RoleId = roles["Sales"],
                Status = UserStatus.Active,
                CreatedAt = DateTime.UtcNow
            };

            var techUser = new User
            {
                Username = "tech",
                FullName = "محمد الصاوي (فني الصيانة المعتمد)",
                Email = "tech@doctor.com",
                PhoneNumber = "01000000003",
                PasswordHash = PasswordHasher.HashPassword("Tech@123"),
                RoleId = roles["Maintenance"],
                Status = UserStatus.Active,
                CreatedAt = DateTime.UtcNow
            };

            context.Users.AddRange(admin, salesUser, techUser);
            await context.SaveChangesAsync();
        }

        // 4. Brands
        if (!await context.Brands.AnyAsync())
        {
            var brands = new List<Brand>
            {
                new Brand { Name = "Apple", IsActive = true },
                new Brand { Name = "Samsung", IsActive = true },
                new Brand { Name = "Xiaomi", IsActive = true },
                new Brand { Name = "Oppo", IsActive = true },
                new Brand { Name = "Realme", IsActive = true },
                new Brand { Name = "Infinix", IsActive = true },
                new Brand { Name = "Honor", IsActive = true },
                new Brand { Name = "Huawei", IsActive = true },
                new Brand { Name = "itel", IsActive = true }
            };
            context.Brands.AddRange(brands);
            await context.SaveChangesAsync();
        }

        var brandMap = await context.Brands.ToDictionaryAsync(b => b.Name, b => b.Id);

        // 5. Product Categories
        if (!await context.ProductCategories.AnyAsync())
        {
            var categories = new List<ProductCategory>
            {
                new ProductCategory { Name = "هواتف ذكية", Description = "أجهزة الموبايل الجديدة والمستعملة" },
                new ProductCategory { Name = "شواحن ومحولات", Description = "شواحن حائط، شواحن سيارة، رؤوس أصلية" },
                new ProductCategory { Name = "كابلات ووصلات", Description = "كابلات شحن سريع ونقل بيانات Type-C وLightning" },
                new ProductCategory { Name = "شاشات حماية واسكرينات", Description = "اسكرينات خصوصية، سيراميك، 9D زجاج" },
                new ProductCategory { Name = "جرابات وكفرات", Description = "جرابات سيليكون، ماج سيف، جلد ضد الصدمات" },
                new ProductCategory { Name = "سماعات وبلوتوث", Description = "سماعات ايربودز، هيدفون، سماعات سلكية" },
                new ProductCategory { Name = "باور بانك", Description = "بطاريات محمولة للشحن السريع" },
                new ProductCategory { Name = "فلاشات وكروت ذاكرة", Description = "كروت ميموري وفلاشات OTG" }
            };
            context.ProductCategories.AddRange(categories);
            await context.SaveChangesAsync();
        }

        var catMap = await context.ProductCategories.ToDictionaryAsync(c => c.Name, c => c.Id);

        // 6. Products
        if (!await context.Products.AnyAsync())
        {
            var products = new List<Product>
            {
                // Devices
                new Product
                {
                    Name = "iPhone 15 Pro Max 256GB تيتانيوم طبيعي",
                    ProductType = ProductType.Device,
                    BrandId = brandMap["Apple"],
                    ProductCategoryId = catMap["هواتف ذكية"],
                    Model = "iPhone 15 Pro Max",
                    Color = "تيتانيوم طبيعي",
                    Storage = "256GB",
                    Ram = "8GB",
                    SerialNumberOrImei = "354896110293841",
                    Barcode = "195949012345",
                    Sku = "DEV-APL-15PM-256",
                    PurchasePrice = 52000m,
                    SellingPrice = 57500m,
                    StockQuantity = 4,
                    MinStockLevel = 2,
                    Notes = "نسخة الشرق الأوسط شريحة فيزيكال + إلكترونية"
                },
                new Product
                {
                    Name = "iPhone 14 128GB سماء الليل (Midnight)",
                    ProductType = ProductType.Device,
                    BrandId = brandMap["Apple"],
                    ProductCategoryId = catMap["هواتف ذكية"],
                    Model = "iPhone 14",
                    Color = "Midnight",
                    Storage = "128GB",
                    Ram = "6GB",
                    SerialNumberOrImei = "359281104829102",
                    Barcode = "195949012346",
                    Sku = "DEV-APL-14-128",
                    PurchasePrice = 31000m,
                    SellingPrice = 34500m,
                    StockQuantity = 6,
                    MinStockLevel = 2,
                    Notes = "ضمان دولي جديد مع العلبة وكافة الملحقات"
                },
                new Product
                {
                    Name = "Samsung Galaxy S24 Ultra 256GB رمادي تيتانيوم",
                    ProductType = ProductType.Device,
                    BrandId = brandMap["Samsung"],
                    ProductCategoryId = catMap["هواتف ذكية"],
                    Model = "Galaxy S24 Ultra",
                    Color = "Titanium Gray",
                    Storage = "256GB",
                    Ram = "12GB",
                    SerialNumberOrImei = "352940192847192",
                    Barcode = "880609123456",
                    Sku = "DEV-SAM-S24U-256",
                    PurchasePrice = 48000m,
                    SellingPrice = 53000m,
                    StockQuantity = 5,
                    MinStockLevel = 2,
                    Notes = "ضمان محلي سامسونج مصر مع القلم الأصلي S-Pen"
                },
                new Product
                {
                    Name = "Samsung Galaxy A55 5G 128GB أزرق ثلجي",
                    ProductType = ProductType.Device,
                    BrandId = brandMap["Samsung"],
                    ProductCategoryId = catMap["هواتف ذكية"],
                    Model = "Galaxy A55 5G",
                    Color = "Awesome Iceblue",
                    Storage = "128GB",
                    Ram = "8GB",
                    SerialNumberOrImei = "358192019482910",
                    Barcode = "880609123457",
                    Sku = "DEV-SAM-A55-128",
                    PurchasePrice = 16500m,
                    SellingPrice = 18500m,
                    StockQuantity = 8,
                    MinStockLevel = 3,
                    Notes = "شاشة Super AMOLED 120Hz مقاوم للماء"
                },
                new Product
                {
                    Name = "Xiaomi Redmi Note 13 Pro 256GB أسود منتصف الليل",
                    ProductType = ProductType.Device,
                    BrandId = brandMap["Xiaomi"],
                    ProductCategoryId = catMap["هواتف ذكية"],
                    Model = "Redmi Note 13 Pro",
                    Color = "Midnight Black",
                    Storage = "256GB",
                    Ram = "8GB",
                    SerialNumberOrImei = "864920192847192",
                    Barcode = "693417771234",
                    Sku = "DEV-XIA-RN13P-256",
                    PurchasePrice = 11200m,
                    SellingPrice = 12700m,
                    StockQuantity = 9,
                    MinStockLevel = 3,
                    Notes = "كاميرا 200 ميجابكسل وشاحن 67 واط في العلبة"
                },

                // Devices (Oppo, Realme, Infinix, Honor)
                new Product
                {
                    Name = "Oppo Reno 11 5G 256GB أخضر مموج",
                    ProductType = ProductType.Device,
                    BrandId = brandMap["Oppo"],
                    ProductCategoryId = catMap["هواتف ذكية"],
                    Model = "Reno 11 5G",
                    Color = "Wave Green",
                    Storage = "256GB",
                    Ram = "12GB",
                    SerialNumberOrImei = "867192019482910",
                    Barcode = "693217771235",
                    Sku = "DEV-OPP-RN11-256",
                    PurchasePrice = 17000m,
                    SellingPrice = 19200m,
                    StockQuantity = 5,
                    MinStockLevel = 2,
                    Notes = "كاميرا بورتريه احترافية وشاحن سوبر فوك 67 واط"
                },
                new Product
                {
                    Name = "Realme 12 Pro+ 5G 256GB أزرق غواصة",
                    ProductType = ProductType.Device,
                    BrandId = brandMap["Realme"],
                    ProductCategoryId = catMap["هواتف ذكية"],
                    Model = "12 Pro+ 5G",
                    Color = "Submarine Blue",
                    Storage = "256GB",
                    Ram = "12GB",
                    SerialNumberOrImei = "869192019482911",
                    Barcode = "693217771236",
                    Sku = "DEV-RLM-12PP-256",
                    PurchasePrice = 18500m,
                    SellingPrice = 20800m,
                    StockQuantity = 4,
                    MinStockLevel = 2,
                    Notes = "عدسة بيريسكوب زوم 120X وظهر جلد فاخر"
                },
                new Product
                {
                    Name = "Infinix Note 40 Pro 256GB ذهبي تيتانيوم",
                    ProductType = ProductType.Device,
                    BrandId = brandMap["Infinix"],
                    ProductCategoryId = catMap["هواتف ذكية"],
                    Model = "Note 40 Pro",
                    Color = "Titan Gold",
                    Storage = "256GB",
                    Ram = "8GB",
                    SerialNumberOrImei = "861192019482912",
                    Barcode = "693217771237",
                    Sku = "DEV-INF-N40P-256",
                    PurchasePrice = 10500m,
                    SellingPrice = 12000m,
                    StockQuantity = 6,
                    MinStockLevel = 2,
                    Notes = "شحن لاسلكي مغناطيسي 20W وشحن سلكي سريع 70W"
                },
                new Product
                {
                    Name = "Honor 90 5G 512GB فضي ماسي",
                    ProductType = ProductType.Device,
                    BrandId = brandMap["Honor"],
                    ProductCategoryId = catMap["هواتف ذكية"],
                    Model = "Honor 90",
                    Color = "Diamond Silver",
                    Storage = "512GB",
                    Ram = "12GB",
                    SerialNumberOrImei = "863192019482913",
                    Barcode = "693217771238",
                    Sku = "DEV-HNR-90-512",
                    PurchasePrice = 19000m,
                    SellingPrice = 21500m,
                    StockQuantity = 3,
                    MinStockLevel = 2,
                    Notes = "كاميرا فائقة 200 ميجابكسل وشاشة حماية العين 3840Hz"
                }
            };

            context.Products.AddRange(products);
            await context.SaveChangesAsync();
        }

        // 7. Spare Parts
        if (!await context.SpareParts.AnyAsync())
        {
            var spareParts = new List<SparePart>
            {
                new SparePart
                {
                    Name = "شاشة أصلية كاملة iPhone 14 Pro OLED مع فريم",
                    PartType = "شاشة",
                    BrandId = brandMap["Apple"],
                    CompatibleModel = "iPhone 14 Pro",
                    Barcode = "SP-IP14P-SCR",
                    Sku = "SP-IP14P-01",
                    PurchasePrice = 7500m,
                    SellingPrice = 9200m,
                    StockQuantity = 4,
                    MinStockLevel = 2,
                    Notes = "شاشة سحب أصلية تدعم التروتون 120Hz ProMotion"
                },
                new SparePart
                {
                    Name = "شاشة أصلية Samsung Galaxy A54 / A55 Super AMOLED",
                    PartType = "شاشة",
                    BrandId = brandMap["Samsung"],
                    CompatibleModel = "Galaxy A54 / A55",
                    Barcode = "SP-SMA54-SCR",
                    Sku = "SP-SMA54-01",
                    PurchasePrice = 2600m,
                    SellingPrice = 3400m,
                    StockQuantity = 6,
                    MinStockLevel = 2,
                    Notes = "شاشة أصلية توكيل تدعم البصمة المدمجة تحت الشاشة"
                },
                new SparePart
                {
                    Name = "بطارية أصلية عالية الكفاءة iPhone 13 (3227 mAh)",
                    PartType = "بطارية",
                    BrandId = brandMap["Apple"],
                    CompatibleModel = "iPhone 13",
                    Barcode = "SP-IP13-BAT",
                    Sku = "SP-IP13-02",
                    PurchasePrice = 1100m,
                    SellingPrice = 1650m,
                    StockQuantity = 8,
                    MinStockLevel = 2,
                    Notes = "خلايا أصلية مع فلاتة برمجة صحة البطارية 100%"
                },
                new SparePart
                {
                    Name = "فلاتة شحن ومايكروفون كاملة Samsung A34 5G",
                    PartType = "فلاتة شحن",
                    BrandId = brandMap["Samsung"],
                    CompatibleModel = "Galaxy A34 5G",
                    Barcode = "SP-SMA34-CHG",
                    Sku = "SP-SMA34-03",
                    PurchasePrice = 150m,
                    SellingPrice = 350m,
                    StockQuantity = 12,
                    MinStockLevel = 3,
                    Notes = "تدعم الشحن السريع ونقل البيانات للصوت والكمبيوتر"
                },
                new SparePart
                {
                    Name = "باغة زجاجية خارجية لشاشة iPhone 11 مع OCA",
                    PartType = "باغة",
                    BrandId = brandMap["Apple"],
                    CompatibleModel = "iPhone 11",
                    Barcode = "SP-IP11-GLS",
                    Sku = "SP-IP11-04",
                    PurchasePrice = 90m,
                    SellingPrice = 250m,
                    StockQuantity = 16,
                    MinStockLevel = 4,
                    Notes = "زجاج عالي النقاء ضد الخدوش مع لاصق OCA مسبقاً"
                },
                new SparePart
                {
                    Name = "كاميرا خلفية أساسية أصلية Redmi Note 12",
                    PartType = "كاميرا",
                    BrandId = brandMap["Xiaomi"],
                    CompatibleModel = "Redmi Note 12",
                    Barcode = "SP-RN12-CAM",
                    Sku = "SP-RN12-05",
                    PurchasePrice = 800m,
                    SellingPrice = 1200m,
                    StockQuantity = 3,
                    MinStockLevel = 1,
                    Notes = "حساس أصلية 50 ميجابكسل تدعم التركيز التلقائي PDAF"
                }
            };

            context.SpareParts.AddRange(spareParts);
            await context.SaveChangesAsync();
        }

        // 8. Settings
        if (!await context.Settings.AnyAsync())
        {
            var settings = new List<Setting>
            {
                new Setting { Key = "CenterName", Value = "الدكتور", Description = "اسم المركز والنشاط", Category = "CenterInfo" },
                new Setting { Key = "CenterPhone", Value = "01001234567", Description = "رقم هاتف المركز الرئيسي", Category = "CenterInfo" },
                new Setting { Key = "CenterWhatsApp", Value = "201001234567", Description = "رقم واتساب المركز الرسمي", Category = "CenterInfo" },
                new Setting { Key = "CenterAddress", Value = "محافظة المنيا - مركز العدوه - شارع مصطفي كامل - بجوار مستشفي كرامه", Description = "عنوان المركز", Category = "CenterInfo" },
                new Setting { Key = "CenterTaxNumber", Value = "458-921-340", Description = "الرقم الضريبي أو السجل التجاري", Category = "CenterInfo" },
                new Setting { Key = "Currency", Value = "ج.م", Description = "العملة المستخدمة في الفواتير", Category = "General" },
                
                // WhatsApp Settings
                new Setting { Key = "WhatsApp_Enabled", Value = "true", Description = "تفعيل إرسال إشعارات الواتساب للعملاء", Category = "WhatsApp" },
                new Setting { Key = "WhatsApp_ApiUrl", Value = "https://api.ultramsg.com/instance123/messages/chat", Description = "رابط بوابة WhatsApp API (مثل UltraMsg أو WPPConnect)", Category = "WhatsApp" },
                new Setting { Key = "WhatsApp_ApiKey", Value = "", Description = "رمز الدخول / Token للبوابة (يتم تركه فارغاً لوضع المحاكاة التفاعلية)", Category = "WhatsApp" },
                new Setting { Key = "WhatsApp_InstanceId", Value = "doctor_instance_01", Description = "معرف النسخة للبوابة", Category = "WhatsApp" },
                new Setting { 
                    Key = "WhatsApp_ReadyTemplate", 
                    Value = "مرحبًا {CustomerName}،\nنود إبلاغك بأن جهازك {Device} أصبح جاهزًا للاستلام بنجاح بعد الصيانة.\nرقم الصيانة: {RepairCode}\nالمبلغ المطلوب: {RemainingAmount} {Currency}\nيمكنك التوجه إلى السنتر للاستلام.\nعنواننا: {CenterAddress}\nشكرًا لاختيارك {CenterName}.", 
                    Description = "قالب رسالة جاهزية الجهاز للاستلام", 
                    Category = "WhatsApp" 
                },
                new Setting { 
                    Key = "WhatsApp_ReceivedTemplate", 
                    Value = "مرحبًا {CustomerName}،\nتم استلام جهازك {Device} بنجاح في قسم الصيانة.\nرقم الصيانة للمتابعة: {RepairCode}\nوصف المشكلة: {ProblemDescription}\nسنقوم بإبلاغك بكافة التحديثات أولاً بأول.\n{CenterName}", 
                    Description = "قالب رسالة استلام الجهاز", 
                    Category = "WhatsApp" 
                }
            };

            context.Settings.AddRange(settings);
            await context.SaveChangesAsync();
        }
        else
        {
            // Sync updated center name and address
            var nameSetting = await context.Settings.FirstOrDefaultAsync(s => s.Key == "CenterName");
            if (nameSetting != null) nameSetting.Value = "الدكتور";
            
            var addrSetting = await context.Settings.FirstOrDefaultAsync(s => s.Key == "CenterAddress");
            if (addrSetting != null) addrSetting.Value = "محافظة المنيا - مركز العدوه - شارع مصطفي كامل - بجوار مستشفي كرامه";

            var phoneSetting = await context.Settings.FirstOrDefaultAsync(s => s.Key == "CenterPhone");
            if (phoneSetting != null) phoneSetting.Value = "01001234567";

            var waSetting = await context.Settings.FirstOrDefaultAsync(s => s.Key == "CenterWhatsApp");
            if (waSetting != null) waSetting.Value = "201001234567";

            await context.SaveChangesAsync();
        }

        // 9. Ensure database tables and structure
        await EnsureCustomTablesAndCleanAccessoriesAsync(context);
    }

    private static async Task EnsureCustomTablesAndCleanAccessoriesAsync(ApplicationDbContext context)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Suppliers' and xtype='U')
BEGIN
    CREATE TABLE [dbo].[Suppliers](
        [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Name] [nvarchar](100) NOT NULL,
        [CompanyName] [nvarchar](100) NULL,
        [PhoneNumber] [nvarchar](20) NOT NULL,
        [WhatsAppNumber] [nvarchar](20) NULL,
        [Address] [nvarchar](250) NULL,
        [Notes] [nvarchar](500) NULL,
        [IsActive] [bit] NOT NULL DEFAULT(1),
        [CreatedAt] [datetime2](7) NOT NULL DEFAULT(GETUTCDATE())
    );
END

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SupplyOrders' and xtype='U')
BEGIN
    CREATE TABLE [dbo].[SupplyOrders](
        [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [OrderNumber] [nvarchar](50) NOT NULL,
        [SupplierId] [int] NOT NULL,
        [UserId] [int] NULL,
        [TotalAmount] [decimal](18, 2) NOT NULL,
        [PaidAmount] [decimal](18, 2) NOT NULL,
        [RemainingAmount] [decimal](18, 2) NOT NULL,
        [Notes] [nvarchar](500) NULL,
        [CreatedAt] [datetime2](7) NOT NULL DEFAULT(GETUTCDATE()),
        CONSTRAINT [FK_SupplyOrders_Suppliers_SupplierId] FOREIGN KEY([SupplierId]) REFERENCES [dbo].[Suppliers] ([Id]) ON DELETE CASCADE
    );
END

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SupplyOrderItems' and xtype='U')
BEGIN
    CREATE TABLE [dbo].[SupplyOrderItems](
        [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [SupplyOrderId] [int] NOT NULL,
        [ProductId] [int] NOT NULL,
        [Quantity] [int] NOT NULL,
        [CostPrice] [decimal](18, 2) NOT NULL,
        [SellingPrice] [decimal](18, 2) NOT NULL,
        [TotalPrice] [decimal](18, 2) NOT NULL,
        CONSTRAINT [FK_SupplyOrderItems_SupplyOrders_SupplyOrderId] FOREIGN KEY([SupplyOrderId]) REFERENCES [dbo].[SupplyOrders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SupplyOrderItems_Products_ProductId] FOREIGN KEY([ProductId]) REFERENCES [dbo].[Products] ([Id])
    );
END

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SupplierPayments' and xtype='U')
BEGIN
    CREATE TABLE [dbo].[SupplierPayments](
        [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [SupplierId] [int] NOT NULL,
        [SupplyOrderId] [int] NULL,
        [Amount] [decimal](18, 2) NOT NULL,
        [PaymentMethod] [int] NOT NULL DEFAULT(1),
        [ReferenceNumber] [nvarchar](100) NULL,
        [Notes] [nvarchar](500) NULL,
        [CreatedAt] [datetime2](7) NOT NULL DEFAULT(GETUTCDATE()),
        CONSTRAINT [FK_SupplierPayments_Suppliers_SupplierId] FOREIGN KEY([SupplierId]) REFERENCES [dbo].[Suppliers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SupplierPayments_SupplyOrders_SupplyOrderId] FOREIGN KEY([SupplyOrderId]) REFERENCES [dbo].[SupplyOrders] ([Id])
    );
END

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='CashShifts' and xtype='U')
BEGIN
    CREATE TABLE [dbo].[CashShifts](
        [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [UserId] [int] NOT NULL,
        [StartTime] [datetime2](7) NOT NULL DEFAULT(GETUTCDATE()),
        [EndTime] [datetime2](7) NULL,
        [StartingCash] [decimal](18, 2) NOT NULL,
        [ActualCash] [decimal](18, 2) NULL,
        [TotalCashSales] [decimal](18, 2) NULL,
        [TotalOtherSales] [decimal](18, 2) NULL,
        [ExpectedCash] [decimal](18, 2) NULL,
        [Difference] [decimal](18, 2) NULL,
        [Status] [int] NOT NULL DEFAULT(1),
        [Notes] [nvarchar](500) NULL,
        CONSTRAINT [FK_CashShifts_Users_UserId] FOREIGN KEY([UserId]) REFERENCES [dbo].[Users] ([Id])
    );
END
");

            // Clean up any accessories from Products table (delete if not in SaleItems, otherwise mark inactive)
            await context.Database.ExecuteSqlRawAsync(@"
                DELETE FROM Products WHERE ProductType = 2 AND Id NOT IN (SELECT ProductId FROM SaleItems);
                UPDATE Products SET IsActive = 0 WHERE ProductType = 2;
            ");
        }
        catch { }
    }
}
