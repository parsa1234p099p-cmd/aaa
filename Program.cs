using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Net.Mail;
using System.Net;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DbSettings>(builder.Configuration.GetSection("Database"));
builder.Services.Configure<UploadSettings>(builder.Configuration.GetSection("UploadSettings"));
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<AdminSettings>(builder.Configuration.GetSection("Admin"));

// ---- تنظیم حجم آپلود (مثلاً تا 200 مگابایت) ----
var maxUploadSize = 1024L * 1024L * 200L; // 200 MB
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = maxUploadSize;
});
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = maxUploadSize;
});

// ---- DbContext + SQLite ----
builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
{
    var dbSettings = sp.GetRequiredService<IOptions<DbSettings>>().Value;
    // اگر مسیر پوشه دیتابیس وجود نداشت بساز
    var conn = dbSettings.ConnectionString;
    // مثال: Data Source=AppData/avayezaryab.db
    var parts = conn.Split("=", StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 2)
    {
        var path = parts[1].Trim();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
    opt.UseSqlite(conn);
});

var app = builder.Build();

// ---- ساخت جداول در اولین اجرا ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// برای نمایش خطاها در توسعه
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();

// ----------------- Helper های ساده -----------------
static string HashPassword(string password)
{
    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
    return Convert.ToHexString(bytes);
}

static bool VerifyPassword(string password, string hash)
    => HashPassword(password) == hash;

static string GenerateCode(int length = 6)
{
    var rnd = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, length));
    return rnd.ToString(new string('0', length));
}

static string NewToken() => Guid.NewGuid().ToString("N");

static async Task SafeSendEmail(
    SmtpSettings smtp,
    string toEmail,
    string subject,
    string body)
{
    if (string.IsNullOrWhiteSpace(smtp.Host))
    {
        Console.WriteLine("SMTP تنظیم نشده است، ایمیل فقط در کنسول چاپ می‌شود.");
        Console.WriteLine($"TO: {toEmail}\nSUBJECT: {subject}\nBODY:\n{body}");
        return;
    }

    try
    {
        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,
            Credentials = new NetworkCredential(smtp.User, smtp.Password)
        };

        var msg = new MailMessage
        {
            From = new MailAddress(smtp.From ?? smtp.User),
            Subject = subject,
            Body = body,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        msg.To.Add(toEmail);

        await client.SendMailAsync(msg);
    }
    catch (Exception ex)
    {
        // برای اینکه سایت نترکه، فقط لاگ می‌کنیم
        Console.WriteLine("SMTP ERROR: " + ex.Message);
        Console.WriteLine($"TO: {toEmail}\nSUBJECT: {subject}\nBODY:\n{body}");
    }
}

// ==================== API های عمومی =======================

// ---- دریافت محتوی معرفی (ویدیو/پوستر) ----
app.MapGet("/api/content", async (AppDbContext db) =>
{
    var intro = await db.IntroContents.FirstOrDefaultAsync();
    if (intro == null)
        return Results.Ok(new { intro = new { } });

    return Results.Ok(new
    {
        intro = new
        {
            intro.VideoUrl,
            intro.PosterUrl
        }
    });
});

// ---- مدیای اساتید ----
app.MapGet("/api/media/teachers", async (AppDbContext db) =>
{
    var list = await db.MediaItems
        .Where(m => m.Type == "Teacher")
        .OrderByDescending(m => m.CreatedAt)
        .ToListAsync();

    return Results.Ok(list.Select(m => new
    {
        m.Id,
        m.TeacherName,
        m.MediaType,
        m.FileUrl,
        m.Caption
    }));
});

// ---- مدیای هنرجویان ----
app.MapGet("/api/media/students", async (AppDbContext db) =>
{
    var list = await db.MediaItems
        .Where(m => m.Type == "Student")
        .OrderByDescending(m => m.CreatedAt)
        .ToListAsync();

    return Results.Ok(list.Select(m => new
    {
        m.Id,
        m.Caption,
        m.MediaType,
        m.FileUrl,
        m.CreatedAt
    }));
});

// ---- لیست کتاب‌ها ----
app.MapGet("/api/books", async (AppDbContext db) =>
{
    var list = await db.Books.OrderBy(b => b.Id).ToListAsync();
    return Results.Ok(list.Select(b => new
    {
        b.Id,
        b.Title,
        b.Level,
        b.Price,
        b.PdfUrl
    }));
});

// ---- خرید (نمونه) ----
app.MapPost("/api/checkout", async (CheckoutRequest reqBody, AppDbContext db) =>
{
    // فعلاً فقط در دیتابیس ذخیره می‌کنیم، بدون بررسی توکن
    foreach (var item in reqBody.Items)
    {
        db.Purchases.Add(new Purchase
        {
            BookId = item.Id,
            Title = item.Title,
            Price = item.Price,
            CreatedAt = DateTime.UtcNow
        });
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { message = "خرید به صورت نمونه ثبت شد." });
});

// ==================== AUTH کاربر =======================

// ثبت‌نام
app.MapPost("/api/auth/register", async (
    RegisterRequest model,
    AppDbContext db,
    IOptions<SmtpSettings> smtpOpt) =>
{
    if (string.IsNullOrWhiteSpace(model.Email) ||
        string.IsNullOrWhiteSpace(model.Username) ||
        string.IsNullOrWhiteSpace(model.Password) ||
        string.IsNullOrWhiteSpace(model.ConfirmPassword))
    {
        return Results.BadRequest(new { message = "همه فیلدها را پر کنید." });
    }

    if (model.Password != model.ConfirmPassword)
        return Results.BadRequest(new { message = "رمز عبور و تکرار آن یکسان نیست." });

    var exists = await db.Users.AnyAsync(u => u.Email == model.Email);
    if (exists)
        return Results.BadRequest(new { message = "ایمیل قبلاً ثبت شده است." });

    var user = new User
    {
        Username = model.Username.Trim(),
        Email = model.Email.Trim(),
        PasswordHash = HashPassword(model.Password),
        CreatedAt = DateTime.UtcNow,
        EmailVerified = false
    };
    db.Users.Add(user);

    // کد تأیید
    var code = GenerateCode(6);
    db.EmailCodes.Add(new EmailVerificationCode
    {
        Email = user.Email,
        Code = code,
        ExpiresAt = DateTime.UtcNow.AddMinutes(15),
        Used = false
    });

    await db.SaveChangesAsync();

    var smtp = smtpOpt.Value;
    var body = $"کد تأیید ایمیل شما در آموزشگاه آوای زریاب:\n\n{code}\n\nاین کد تا ۱۵ دقیقه معتبر است.";
    await SafeSendEmail(smtp, user.Email, "کد تأیید ایمیل - آوای زریاب", body);

    return Results.Ok(new { message = "ثبت‌نام اولیه انجام شد. کد تأیید به ایمیل شما ارسال شد." });
});

// تأیید ایمیل
app.MapPost("/api/auth/verify-email", async (
    VerifyEmailRequest model,
    AppDbContext db) =>
{
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == model.Email);
    if (user == null)
        return Results.BadRequest(new { message = "کاربر یافت نشد." });

    var codeEntity = await db.EmailCodes
        .Where(c => c.Email == model.Email && c.Code == model.Code && !c.Used)
        .OrderByDescending(c => c.Id)
        .FirstOrDefaultAsync();

    if (codeEntity == null || codeEntity.ExpiresAt < DateTime.UtcNow)
        return Results.BadRequest(new { message = "کد تأیید نامعتبر یا منقضی شده است." });

    codeEntity.Used = true;
    user.EmailVerified = true;
    await db.SaveChangesAsync();

    var token = NewToken();
    // (در این نسخه، توکن فقط به فرانت داده می‌شود و جایی ذخیره نمی‌کنیم)

    return Results.Ok(new
    {
        token,
        user = new { user.Id, user.Username, user.Email }
    });
});

// ورود
app.MapPost("/api/auth/login", async (
    LoginRequest model,
    AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(model.Identifier) ||
        string.IsNullOrWhiteSpace(model.Password))
    {
        return Results.BadRequest(new { message = "ایمیل/نام کاربری و رمز را وارد کنید." });
    }

    var user = await db.Users
        .Where(u => u.Email == model.Identifier || u.Username == model.Identifier)
        .SingleOrDefaultAsync();

    if (user == null || !VerifyPassword(model.Password, user.PasswordHash))
        return Results.BadRequest(new { message = "ایمیل/نام کاربری یا رمز نادرست است." });

    var token = NewToken();

    return Results.Ok(new
    {
        token,
        user = new { user.Id, user.Username, user.Email }
    });
});

// فراموشی رمز
app.MapPost("/api/auth/forgot-password", async (
    ForgotPasswordRequest model,
    AppDbContext db,
    IOptions<SmtpSettings> smtpOpt) =>
{
    if (string.IsNullOrWhiteSpace(model.Email))
        return Results.BadRequest(new { message = "ایمیل را وارد کنید." });

    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == model.Email);
    if (user != null)
    {
        var code = GenerateCode(6);
        db.ResetCodes.Add(new PasswordResetCode
        {
            Email = user.Email,
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            Used = false
        });
        await db.SaveChangesAsync();

        var smtp = smtpOpt.Value;
        var body = $"کد بازیابی رمز عبور شما:\n\n{code}\n\nاین کد تا ۱۵ دقیقه معتبر است.";
        await SafeSendEmail(smtp, user.Email, "کد بازیابی رمز عبور - آوای زریاب", body);
    }

    return Results.Ok(new { message = "اگر ایمیل صحیح باشد، کد بازیابی ارسال می‌شود." });
});

// تغییر رمز با کد
app.MapPost("/api/auth/reset-password", async (
    ResetPasswordRequest model,
    AppDbContext db) =>
{
    if (model.NewPassword != model.ConfirmPassword)
        return Results.BadRequest(new { message = "رمز جدید و تکرار آن یکسان نیست." });

    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == model.Email);
    if (user == null)
        return Results.BadRequest(new { message = "کاربر یافت نشد." });

    var codeEntity = await db.ResetCodes
        .Where(c => c.Email == model.Email && c.Code == model.Code && !c.Used)
        .OrderByDescending(c => c.Id)
        .FirstOrDefaultAsync();

    if (codeEntity == null || codeEntity.ExpiresAt < DateTime.UtcNow)
        return Results.BadRequest(new { message = "کد بازیابی نامعتبر یا منقضی شده است." });

    codeEntity.Used = true;
    user.PasswordHash = HashPassword(model.NewPassword);
    await db.SaveChangesAsync();

    return Results.Ok(new { message = "رمز عبور با موفقیت تغییر کرد." });
});

// ==================== API های ادمین =======================

static bool CheckAdmin(HttpRequest req, AdminSettings admin)
{
    if (!req.Headers.TryGetValue("X-Admin-Token", out var token))
        return false;
    return token == admin.Token;
}

// ورود ادمین
app.MapPost("/api/admin/login", async (
    AdminLoginRequest model,
    IOptions<AdminSettings> adminOpt) =>
{
    var admin = adminOpt.Value;
    if (model.Email == admin.Email && model.Password == admin.Password)
    {
        return Results.Ok(new { token = admin.Token });
    }
    return Results.BadRequest(new { message = "ایمیل یا رمز ادمین نادرست است." });
});

// آپلود ویدیو معرفی
app.MapPost("/api/admin/upload-intro-video", async (
    HttpRequest req,
    IWebHostEnvironment env,
    IOptions<UploadSettings> upOpt,
    IOptions<AdminSettings> adminOpt,
    AppDbContext db) =>
{
    if (!CheckAdmin(req, adminOpt.Value))
        return Results.Unauthorized();

    var form = await req.ReadFormAsync();
    var file = form.Files["file"];
    if (file == null || file.Length == 0)
        return Results.BadRequest(new { message = "فایلی انتخاب نشده است." });

    var webRoot = env.WebRootPath ?? "wwwroot";
    var uploadsRoot = Path.Combine(webRoot, "uploads", "intro");
    Directory.CreateDirectory(uploadsRoot);

    var ext = Path.GetExtension(file.FileName);
    if (string.IsNullOrWhiteSpace(ext)) ext = ".mp4";
    var fileName = "intro_video" + ext;
    var savePath = Path.Combine(uploadsRoot, fileName);

    using (var stream = new FileStream(savePath, FileMode.Create))
        await file.CopyToAsync(stream);

    var baseUrl = upOpt.Value.BaseUrl.TrimEnd('/');
    var url = $"{baseUrl}/intro/{fileName}".Replace("\\", "/");

    var intro = await db.IntroContents.FirstOrDefaultAsync();
    if (intro == null)
    {
        intro = new IntroContent();
        db.IntroContents.Add(intro);
    }
    intro.VideoUrl = url;
    await db.SaveChangesAsync();

    return Results.Ok(new { message = "ویدیو معرفی ذخیره شد.", url });
});

// آپلود پوستر معرفی
app.MapPost("/api/admin/upload-intro-poster", async (
    HttpRequest req,
    IWebHostEnvironment env,
    IOptions<UploadSettings> upOpt,
    IOptions<AdminSettings> adminOpt,
    AppDbContext db) =>
{
    if (!CheckAdmin(req, adminOpt.Value))
        return Results.Unauthorized();

    var form = await req.ReadFormAsync();
    var file = form.Files["file"];
    if (file == null || file.Length == 0)
        return Results.BadRequest(new { message = "فایلی انتخاب نشده است." });

    var webRoot = env.WebRootPath ?? "wwwroot";
    var uploadsRoot = Path.Combine(webRoot, "uploads", "intro");
    Directory.CreateDirectory(uploadsRoot);

    var ext = Path.GetExtension(file.FileName);
    if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
    var fileName = "intro_poster" + ext;
    var savePath = Path.Combine(uploadsRoot, fileName);

    using (var stream = new FileStream(savePath, FileMode.Create))
        await file.CopyToAsync(stream);

    var baseUrl = upOpt.Value.BaseUrl.TrimEnd('/');
    var url = $"{baseUrl}/intro/{fileName}".Replace("\\", "/");

    var intro = await db.IntroContents.FirstOrDefaultAsync();
    if (intro == null)
    {
        intro = new IntroContent();
        db.IntroContents.Add(intro);
    }
    intro.PosterUrl = url;
    await db.SaveChangesAsync();

    return Results.Ok(new { message = "پوستر معرفی ذخیره شد.", url });
});

// آپلود مدیای اساتید
app.MapPost("/api/admin/teacher-media", async (
    HttpRequest req,
    IWebHostEnvironment env,
    IOptions<UploadSettings> upOpt,
    IOptions<AdminSettings> adminOpt,
    AppDbContext db) =>
{
    if (!CheckAdmin(req, adminOpt.Value))
        return Results.Unauthorized();

    var form = await req.ReadFormAsync();
    var teacherName = form["teacherName"].ToString();
    if (string.IsNullOrWhiteSpace(teacherName))
        return Results.BadRequest(new { message = "نام استاد خالی است." });

    var files = form.Files;
    if (files == null || files.Count == 0)
        return Results.BadRequest(new { message = "هیچ فایلی انتخاب نشده است." });

    var webRoot = env.WebRootPath ?? "wwwroot";
    var uploadsRoot = Path.Combine(webRoot, "uploads", "teachers");
    Directory.CreateDirectory(uploadsRoot);

    var baseUrl = upOpt.Value.BaseUrl.TrimEnd('/');

    foreach (var file in files)
    {
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".dat";
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var savePath = Path.Combine(uploadsRoot, fileName);

        using (var stream = new FileStream(savePath, FileMode.Create))
            await file.CopyToAsync(stream);

        var mime = file.ContentType.ToLowerInvariant();
        var mediaType = mime.StartsWith("video") ? "video" : "image";
        var url = $"{baseUrl}/teachers/{fileName}".Replace("\\", "/");

        db.MediaItems.Add(new MediaItem
        {
            Type = "Teacher",
            TeacherName = teacherName,
            MediaType = mediaType,
            FileUrl = url,
            Caption = null,
            CreatedAt = DateTime.UtcNow
        });
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { message = "مدیای استاد ثبت شد." });
});

// آپلود مدیای هنرجویان
app.MapPost("/api/admin/student-media", async (
    HttpRequest req,
    IWebHostEnvironment env,
    IOptions<UploadSettings> upOpt,
    IOptions<AdminSettings> adminOpt,
    AppDbContext db) =>
{
    if (!CheckAdmin(req, adminOpt.Value))
        return Results.Unauthorized();

    var form = await req.ReadFormAsync();
    var caption = form["caption"].ToString();

    var files = form.Files;
    if (files == null || files.Count == 0)
        return Results.BadRequest(new { message = "هیچ فایلی انتخاب نشده است." });

    var webRoot = env.WebRootPath ?? "wwwroot";
    var uploadsRoot = Path.Combine(webRoot, "uploads", "students");
    Directory.CreateDirectory(uploadsRoot);

    var baseUrl = upOpt.Value.BaseUrl.TrimEnd('/');

    foreach (var file in files)
    {
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".dat";
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var savePath = Path.Combine(uploadsRoot, fileName);

        using (var stream = new FileStream(savePath, FileMode.Create))
            await file.CopyToAsync(stream);

        var mime = file.ContentType.ToLowerInvariant();
        var mediaType = mime.StartsWith("video") ? "video" : "image";
        var url = $"{baseUrl}/students/{fileName}".Replace("\\", "/");

        db.MediaItems.Add(new MediaItem
        {
            Type = "Student",
            TeacherName = null,
            MediaType = mediaType,
            FileUrl = url,
            Caption = caption,
            CreatedAt = DateTime.UtcNow
        });
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { message = "مدیای هنرجویان ثبت شد." });
});

// ثبت کتاب
app.MapPost("/api/admin/books", async (
    AdminAddBookRequest model,
    HttpRequest req,
    IOptions<AdminSettings> adminOpt,
    AppDbContext db) =>
{
    if (!CheckAdmin(req, adminOpt.Value))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(model.Title) || string.IsNullOrWhiteSpace(model.PdfUrl))
        return Results.BadRequest(new { message = "عنوان و آدرس PDF اجباری است." });

    var book = new Book
    {
        Title = model.Title.Trim(),
        Level = string.IsNullOrWhiteSpace(model.Level) ? null : model.Level.Trim(),
        Price = model.Price,
        PdfUrl = model.PdfUrl.Trim()
    };
    db.Books.Add(book);
    await db.SaveChangesAsync();

    return Results.Ok(new { message = "کتاب ثبت شد." });
});

// آپلود لوگو
app.MapPost("/api/admin/upload-logo", async (
    HttpRequest req,
    IWebHostEnvironment env,
    IOptions<UploadSettings> upOpt,
    IOptions<AdminSettings> adminOpt) =>
{
    if (!CheckAdmin(req, adminOpt.Value))
        return Results.Unauthorized();

    var form = await req.ReadFormAsync();
    var file = form.Files["file"];
    if (file == null || file.Length == 0)
        return Results.BadRequest(new { message = "فایلی انتخاب نشده است." });

    var webRoot = env.WebRootPath ?? "wwwroot";
    var root = Path.Combine(webRoot, "uploads", "branding");
    Directory.CreateDirectory(root);

    var ext = Path.GetExtension(file.FileName);
    if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
    var fileName = "logo" + ext;
    var savePath = Path.Combine(root, fileName);

    using (var stream = new FileStream(savePath, FileMode.Create))
        await file.CopyToAsync(stream);

    var baseUrl = upOpt.Value.BaseUrl.TrimEnd('/');
    var url = $"{baseUrl}/branding/{fileName}".Replace("\\", "/");

    return Results.Ok(new { message = "لوگو ذخیره شد.", url });
});

// آپلود عکس پروفایل اساتید
app.MapPost("/api/admin/teacher-avatar", async (
    HttpRequest req,
    IWebHostEnvironment env,
    IOptions<UploadSettings> upOpt,
    IOptions<AdminSettings> adminOpt) =>
{
    if (!CheckAdmin(req, adminOpt.Value))
        return Results.Unauthorized();

    var form = await req.ReadFormAsync();
    var teacherKey = form["teacherKey"].ToString();
    var file = form.Files["file"];

    if (string.IsNullOrWhiteSpace(teacherKey))
        return Results.BadRequest(new { message = "استاد انتخاب نشده است." });
    if (file == null || file.Length == 0)
        return Results.BadRequest(new { message = "فایلی انتخاب نشده است." });

    var webRoot = env.WebRootPath ?? "wwwroot";
    var root = Path.Combine(webRoot, "uploads", "teachers", "avatars");
    Directory.CreateDirectory(root);

    var ext = Path.GetExtension(file.FileName);
    if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
    var fileName = teacherKey + ext;
    var savePath = Path.Combine(root, fileName);

    using (var stream = new FileStream(savePath, FileMode.Create))
        await file.CopyToAsync(stream);

    var baseUrl = upOpt.Value.BaseUrl.TrimEnd('/');
    var url = $"{baseUrl}/teachers/avatars/{fileName}".Replace("\\", "/");

    return Results.Ok(new { message = "عکس پروفایل استاد ذخیره شد.", url });
});

// -----------------------------------------------------
app.Run();


// ==================== کلاس‌های مدل و تنظیمات =======================

public class DbSettings
{
    public string ConnectionString { get; set; } = "Data Source=AppData/avayezaryab.db";
}

public class UploadSettings
{
    // مثلا: "/uploads" یا "https://example.com/uploads"
    public string BaseUrl { get; set; } = "/uploads";
}

public class SmtpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
}

public class AdminSettings
{
    public string Email { get; set; } = "admin@example.com";
    public string Password { get; set; } = "Admin123!";
    public string Token { get; set; } = "SUPER_SECRET_ADMIN_TOKEN";
}

// ---- EF Core DbContext ----
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<EmailVerificationCode> EmailCodes => Set<EmailVerificationCode>();
    public DbSet<PasswordResetCode> ResetCodes => Set<PasswordResetCode>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<IntroContent> IntroContents => Set<IntroContent>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
}

// ---- Entities ----

public class User
{
    public int Id { get; set; }
    [MaxLength(100)]
    public string Username { get; set; } = "";
    [MaxLength(200)]
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool EmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class EmailVerificationCode
{
    public int Id { get; set; }
    [MaxLength(200)]
    public string Email { get; set; } = "";
    [MaxLength(20)]
    public string Code { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; }
}

public class PasswordResetCode
{
    public int Id { get; set; }
    [MaxLength(200)]
    public string Email { get; set; } = "";
    [MaxLength(20)]
    public string Code { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; }
}

public class Book
{
    public int Id { get; set; }
    [MaxLength(200)]
    public string Title { get; set; } = "";
    [MaxLength(100)]
    public string? Level { get; set; }
    public int Price { get; set; }
    [MaxLength(500)]
    public string PdfUrl { get; set; } = "";
}

public class MediaItem
{
    public int Id { get; set; }
    // Teacher / Student
    [MaxLength(20)]
    public string Type { get; set; } = "";
    [MaxLength(200)]
    public string? TeacherName { get; set; }
    // image / video
    [MaxLength(20)]
    public string MediaType { get; set; } = "";
    [MaxLength(500)]
    public string FileUrl { get; set; } = "";
    [MaxLength(500)]
    public string? Caption { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class IntroContent
{
    public int Id { get; set; }
    [MaxLength(500)]
    public string? VideoUrl { get; set; }
    [MaxLength(500)]
    public string? PosterUrl { get; set; }
}

public class Purchase
{
    public int Id { get; set; }
    public int BookId { get; set; }
    [MaxLength(200)]
    public string Title { get; set; } = "";
    public int Price { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ---- DTO ها ----

public record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string ConfirmPassword);

public record VerifyEmailRequest(
    string Email,
    string Code);

public record LoginRequest(
    string Identifier,
    string Password);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(
    string Email,
    string Code,
    string NewPassword,
    string ConfirmPassword);

public record AdminLoginRequest(
    string Email,
    string Password);

public record AdminAddBookRequest(
    string Title,
    string? Level,
    int Price,
    string PdfUrl);

public record CheckoutItem(int Id, string Title, int Price);
public record CheckoutRequest(List<CheckoutItem> Items);
